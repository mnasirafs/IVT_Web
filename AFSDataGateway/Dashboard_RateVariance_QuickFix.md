# Dashboard Rate Variance Fix - Quick Reference

## Problem Identified
The rate variance calculations are using incorrect property names and formulas. They should use the new `LW_*` last-week properties from your updated controller.

## Root Cause
- Old code: Used separate `tma` (Admin model) for last week data
- New code: Should use `tm.LW_*` properties in the same model

## Quick Fix Summary

### For Exception Rate Card:
**BEFORE:**
```razor
var curRate = tm != null ? (double?)(tm.ExceptionRate ?? 0) : null;
var prevRate = tma != null ? (double?)(tma.LWExceptionRate ?? 0) : null;
```

**AFTER:**
```razor
var curRate = tm != null ? (tm.ExceptionRate ?? 0m) : 0m;
var prevRate = tm != null ? (tm.LW_ExceptionRate ?? 0m) : 0m;
var ratePP = curRate - prevRate;  // Variance in percentage points
```

---

### For Ancillaries Rate Card:
**BEFORE:**
```razor
var curA = tm != null ? (double?)(tm.AncillariesRate ?? 0) : null;
var prevA = tma != null ? (double?)(tma.LWAncillariesRate ?? 0) : null;
```

**AFTER:**
```razor
var curARate = tm != null ? (tm.AncillariesRate ?? 0m) : 0m;
var prevARate = tm != null ? (tm.LW_AncillariesRate ?? 0m) : 0m;
var ancillariesPP = curARate - prevARate;  // Variance in percentage points
```

---

### For Manual Entry Rate Card (NEW):
```razor
<div class="col-lg-3 mb-3">
	<div class="top-card">
		<div class="left">
			<div class="icon">⌨️</div>
			<div class="meta">
				<div class="title">Manual Entry</div>
				<div class="value">@(tm != null ? String.Format("{0:N0}", tm.TotalManualEntry) : "N/A")</div>
			</div>
		</div>
		<div class="right">
			<div class="label small-muted">Manual Entry Rate</div>
			@{
				var curMERate = tm != null ? (tm.CurrentWeek_ManualEntryRate ?? 0m) : 0m;
				var prevMERate = tm != null ? (tm.LW_CurrentWeek_ManualEntryRate ?? 0m) : 0m;
				var manualEntryPP = curMERate - prevMERate;
			}
			<div class="rate">@String.Format("{0:N2}%", curMERate)</div>
			<div class="@(manualEntryPP >= 0 ? "delta positive" : "delta negative")">
				@(manualEntryPP >= 0 ? "+" : "")@String.Format("{0:N2} pp", manualEntryPP)
			</div>
			<div class="small-muted">vs Last Week (@String.Format("{0:N2}%", prevMERate))</div>
		</div>
	</div>
</div>
```

---

### For Not In MoveIT Rate Card (NEW):
```razor
<div class="col-lg-3 mb-3">
	<div class="top-card">
		<div class="left">
			<div class="icon">❌</div>
			<div class="meta">
				<div class="title">Not In MoveIT</div>
				<div class="value">@(tm != null ? String.Format("{0:N0}", tm.TotalNotInMoveit) : "N/A")</div>
			</div>
		</div>
		<div class="right">
			<div class="label small-muted">MoveIT Rate</div>
			@{
				var curNMRate = tm != null ? (tm.CurrentWeek_NotInMoveitRate ?? 0m) : 0m;
				var prevNMRate = tm != null ? (tm.LW_CurrentWeek_NotInMoveitRate ?? 0m) : 0m;
				var notMoveitPP = curNMRate - prevNMRate;
			}
			<div class="rate">@String.Format("{0:N2}%", curNMRate)</div>
			<div class="@(notMoveitPP >= 0 ? "delta positive" : "delta negative")">
				@(notMoveitPP >= 0 ? "+" : "")@String.Format("{0:N2} pp", notMoveitPP)
			</div>
			<div class="small-muted">vs Last Week (@String.Format("{0:N2}%", prevNMRate))</div>
		</div>
	</div>
</div>
```

---

## Key Changes Explained

### 1. **Property Names**
| Old | New | Reason |
|-----|-----|--------|
| `tma.LWExceptionRate` | `tm.LW_ExceptionRate` | Consistent naming with updated model |
| `tma.LWAncillariesRate` | `tm.LW_AncillariesRate` | Uses single model instance |
| N/A | `tm.LW_CurrentWeek_ManualEntryRate` | New property from controller |
| N/A | `tm.LW_CurrentWeek_NotInMoveitRate` | New property from controller |

### 2. **Variance Formula**
```
CORRECT: 
  variancePP = currentRate - lastWeekRate

EXAMPLE:
  Current: 5.25%
  Last Week: 4.80%
  Variance: 5.25 - 4.80 = +0.45 pp
```

### 3. **Display Format**
```
Current Rate: 5.25%
Variance: +0.45 pp
Last Week Reference: (4.80%)
```

### 4. **Color Coding**
```
✅ Green (+): Improvement
   - For good metrics (lower is better): Negative variance is green
   - For bad metrics (lower is better): Negative variance is green

❌ Red (-): Decline
   - Opposite of improvement
```

---

## Implementation Steps

1. **Open**: `AFSDataGateway\Views\IVT\Dashboard.cshtml`

2. **Find** each rate card section (Exception Rate, Ancillaries, etc.)

3. **Replace** the variable declarations with the new formulas above

4. **Update** the display line from:
   ```razor
   <div class="@((pp)>=0?"delta positive":"delta negative")">
   ```
   to:
   ```razor
   <div class="@(ratePP >= 0 ? "delta positive" : "delta negative")">
   ```

5. **Update** the format string to show "pp" (percentage points):
   ```razor
   @String.Format("{0:N2} pp", ratePP)
   ```

6. **Add** the last week rate reference in small-muted text:
   ```razor
   <div class="small-muted">vs Last Week (@String.Format("{0:N2}%", prevRate))</div>
   ```

---

## Model Property Requirements

Ensure your `vDash_TotalMeasures` model has these properties:

```csharp
// Current Week Rates
public decimal? ExceptionRate { get; set; }
public decimal? AncillariesRate { get; set; }
public decimal? CurrentWeek_ManualEntryRate { get; set; }
public decimal? CurrentWeek_NotInMoveitRate { get; set; }

// Last Week Rates (NEW)
public decimal? LW_ExceptionRate { get; set; }
public decimal? LW_AncillariesRate { get; set; }
public decimal? LW_CurrentWeek_ManualEntryRate { get; set; }
public decimal? LW_CurrentWeek_NotInMoveitRate { get; set; }

// Count Data for context
public decimal? TotalExceptions { get; set; }
public decimal? TotalAncillaries { get; set; }
public decimal? TotalManualEntry { get; set; }
public decimal? TotalNotInMoveit { get; set; }
```

---

## Testing

After applying fixes, verify:

- [ ] Exception rate variance displays correctly with "pp"
- [ ] Ancillaries rate variance displays correctly with "pp"
- [ ] Manual Entry rate card appears with correct variance
- [ ] Not In MoveIT rate card appears with correct variance
- [ ] Green color appears for positive changes
- [ ] Red color appears for negative changes
- [ ] Last week rate shows in parentheses for reference
- [ ] No null reference exceptions

---

## Example Output

**Before Fix:**
```
Exception Rate: 5.25%
+0.45 (WRONG - looks like percentage, not percentage points)
vs Last Week
```

**After Fix:**
```
Exception Rate: 5.25%
+0.45 pp (CORRECT - percentage points)
vs Last Week (4.80%)
```