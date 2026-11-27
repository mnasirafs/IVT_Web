"""Thin, parameterised SQL Server access (pyodbc). All SQL in this package uses ? placeholders."""
from __future__ import annotations

import contextlib
import logging
from typing import Any, Iterable, Iterator, List, Optional, Sequence

import pandas as pd

log = logging.getLogger(__name__)


class Database:
    def __init__(self, conn_str: str, timeout: int = 30, command_timeout: int = 600):
        if not conn_str:
            raise ValueError("No database connection configured (set IVT_ML_DB_CONN or IVT_ML_DB_SERVER).")
        self._conn_str = conn_str
        self._timeout = timeout
        self._command_timeout = command_timeout

    def connect(self):
        import pyodbc

        cn = pyodbc.connect(self._conn_str, timeout=self._timeout, autocommit=False)
        cn.timeout = self._command_timeout
        return cn

    def read_df(self, sql: str, params: Sequence[Any] = (), chunksize: Optional[int] = None) -> pd.DataFrame:
        with contextlib.closing(self.connect()) as cn:
            cur = cn.cursor()
            cur.execute(sql, list(params))
            cols = [c[0] for c in cur.description]
            frames: List[pd.DataFrame] = []
            while True:
                rows = cur.fetchmany(chunksize or 50000)
                if not rows:
                    break
                frames.append(pd.DataFrame.from_records([tuple(r) for r in rows], columns=cols))
            cn.rollback()  # read-only: never leave an open transaction
            return pd.concat(frames, ignore_index=True) if frames else pd.DataFrame(columns=cols)

    def scalar(self, sql: str, params: Sequence[Any] = ()) -> Any:
        with contextlib.closing(self.connect()) as cn:
            cur = cn.cursor()
            cur.execute(sql, list(params))
            row = cur.fetchone()
            cn.rollback()
            return row[0] if row else None

    @contextlib.contextmanager
    def transaction(self) -> Iterator[Any]:
        """Yields a cursor inside one transaction; commits on success, rolls back on any error."""
        cn = self.connect()
        try:
            cur = cn.cursor()
            cur.fast_executemany = True
            yield cur
            cn.commit()
        except Exception:
            cn.rollback()
            raise
        finally:
            cn.close()


def insert_rows(cursor: Any, table: str, columns: Sequence[str], rows: Iterable[Sequence[Any]], batch: int = 5000) -> int:
    """Batched parameterised INSERT. Table/column names must come from code constants, never user input."""
    col_sql = ", ".join(f"[{c}]" for c in columns)
    sql = f"INSERT INTO {table} ({col_sql}) VALUES ({', '.join('?' for _ in columns)})"
    buffer: List[Sequence[Any]] = []
    total = 0
    for row in rows:
        buffer.append(tuple(_py(v) for v in row))
        if len(buffer) >= batch:
            cursor.executemany(sql, buffer)
            total += len(buffer)
            buffer.clear()
    if buffer:
        cursor.executemany(sql, buffer)
        total += len(buffer)
    return total


def _py(v: Any) -> Any:
    """Convert pandas/numpy scalars to plain Python for pyodbc."""
    if v is None:
        return None
    try:
        if pd.isna(v):
            return None
    except (TypeError, ValueError):
        pass
    if hasattr(v, "item"):
        return v.item()
    if isinstance(v, pd.Timestamp):
        return v.to_pydatetime()
    return v
