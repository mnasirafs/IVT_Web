using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AFSDataGateway.Services.IvtAdmin
{
    public enum IvtFileLoadKind { BulkHold, BulkRelease, BmsCheck, ReferenceData }

    public sealed class IvtFileTemplate
    {
        public IvtFileLoadKind Kind { get; set; }
        public string Title { get; set; }
        public IvtLandingFolder Folder { get; set; }
        public string[] RequiredColumns { get; set; }   // null = template not verified; uploads disabled
        public bool IntegerJobNumbers { get; set; }
        public string Notes { get; set; }
    }

    // File-based administrative loads. Files land in fixed server-configured folders; the existing SQL Agent jobs load them.
    // Templates were verified from the loader script (JobNumberToCheck_File_Load.py run by IVT_Hold_Release_Check, 2026-10-09).
    public sealed class FileLoadService
    {
        public const string AdminLoadLock = "admin-file-load";
        public static readonly IReadOnlyList<IvtFileTemplate> Templates = new List<IvtFileTemplate>
        {
            new IvtFileTemplate { Kind = IvtFileLoadKind.BulkHold, Title = "Bulk hold", Folder = IvtLandingFolder.HoldConnotes, RequiredColumns = new[] { "jno", "Connote", "Comment" }, IntegerJobNumbers = true,
                Notes = "CSV with header jno,Connote,Comment. Rows without jno are skipped by the loader; jno must be a whole number. The loader holds the connotes and emails the CC contacts." },
            new IvtFileTemplate { Kind = IvtFileLoadKind.BulkRelease, Title = "Bulk release", Folder = IvtLandingFolder.ReleaseConnotes, RequiredColumns = new[] { "jno", "Connote", "Justification" }, IntegerJobNumbers = true,
                Notes = "CSV with header jno,Connote,Justification. jno must be a whole number. The loader releases the connotes and emails the CC contacts." },
            new IvtFileTemplate { Kind = IvtFileLoadKind.BmsCheck, Title = "BMS check file", Folder = IvtLandingFolder.BmsCheck, RequiredColumns = new[] { "jno" },
                Notes = "CSV with a jno column (BMS job numbers). Loaded into tmp.BMS_Check (replacing the previous file) for the BMS reconciliation reports." },
            new IvtFileTemplate { Kind = IvtFileLoadKind.ReferenceData, Title = "Reference data (fuel / prefix)", Folder = IvtLandingFolder.ReferenceData, RequiredColumns = null,
                Notes = "Loaded by SSIS package Prod_REF_Data_Update.dtsx (job IVT_REF_DATA_LOAD). Its flat-file names are not exposed as package parameters, so the template is UNVERIFIED: place the file in the folder as before; upload is disabled here." },
        };

        private readonly IvtAdminRuntime rt;
        public FileLoadService(IvtAdminRuntime runtime) { rt = runtime; }

        public static IvtFileTemplate Template(IvtFileLoadKind kind) { return Templates.First(t => t.Kind == kind); }

        public string JobFor(IvtFileLoadKind kind)
        {
            return kind == IvtFileLoadKind.ReferenceData ? rt.Settings.Jobs.ReferenceDataLoad : rt.Settings.Jobs.AdminFileLoad;
        }

        public string Pattern(IvtFileLoadKind kind) { return kind == IvtFileLoadKind.ReferenceData ? rt.Settings.Paths.ReferenceDataFilePattern : "*.csv"; }

        // The shared admin-load job processes all three folders in one run.
        public static IvtFileLoadKind[] SharedJobKinds = { IvtFileLoadKind.BmsCheck, IvtFileLoadKind.BulkHold, IvtFileLoadKind.BulkRelease };

        public Dictionary<IvtFileLoadKind, IReadOnlyList<IvtLandingFile>> Pending(IEnumerable<IvtFileLoadKind> kinds)
        {
            return kinds.ToDictionary(k => k, k => rt.Landing.List(Template(k).Folder, Pattern(k)));
        }

        // Validates an upload against the verified template. Returns the problems found (empty = valid).
        public static List<string> Validate(IvtFileTemplate template, string fileName, byte[] content, int maxBytes)
        {
            var problems = new List<string>();
            if (template.RequiredColumns == null) { problems.Add("Uploads are disabled for " + template.Title + " because its file template is not verified."); return problems; }
            try { FileLandingStore.ValidateFileName(fileName, ".csv"); } catch (IvtOperationException ex) { problems.Add(ex.Message); return problems; }
            if (content == null || content.Length == 0) { problems.Add("The file is empty."); return problems; }
            if (content.Length > maxBytes) { problems.Add("The file is larger than " + (maxBytes / 1024 / 1024) + " MB."); return problems; }
            string text;
            try { text = new UTF8Encoding(false, true).GetString(content); }
            catch (DecoderFallbackException) { text = Encoding.GetEncoding(1252).GetString(content); }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            List<List<string>> rows;
            try { rows = IvtCsv.Parse(text); } catch (IvtOperationException ex) { problems.Add(ex.Message); return problems; }
            rows = rows.Where(r => r.Any(c => c.Trim().Length > 0)).ToList();
            if (rows.Count < 2) { problems.Add("The file needs a header row and at least one data row."); return problems; }
            var header = rows[0].Select(h => h.Trim()).ToList();
            // The loader selects columns by exact (case-sensitive) name.
            var missing = template.RequiredColumns.Where(c => !header.Contains(c)).ToList();
            if (missing.Count > 0) { problems.Add("Missing column(s): " + string.Join(", ", missing) + ". Header found: " + string.Join(", ", header) + ". Names are case-sensitive."); return problems; }
            var jno = header.IndexOf("jno");
            var withJob = 0;
            for (var i = 1; i < rows.Count && problems.Count < 15; i++)
            {
                var value = jno < rows[i].Count ? rows[i][jno].Trim() : "";
                if (value.Length == 0) continue;
                withJob++;
                if (template.IntegerJobNumbers && !Regex.IsMatch(value, @"^\d{1,18}$")) problems.Add("Row " + (i + 1) + ": jno '" + value + "' is not a whole number.");
            }
            if (withJob == 0) problems.Add("No row has a jno value.");
            return problems;
        }

        public async Task<IvtActionResult> UploadAsync(IvtFileLoadKind kind, string fileName, byte[] content, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            var template = Template(kind);
            var problems = Validate(template, fileName, content, rt.Settings.MaxUploadBytes);
            if (problems.Count > 0) return IvtActionResult.Rejected("The file was not published. " + string.Join(" ", problems), correlation);
            var audited = await AuditedOperation.BeginAsync(rt.Audit, new IvtAuditEntry
                { CorrelationId = correlation, Operator = op.Login, Environment = "production", Operation = "FILE.UPLOAD." + kind.ToString().ToUpperInvariant(), Target = fileName, AffectedCount = 1 }, cancellation).ConfigureAwait(false);
            var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
            try
            {
                rt.Landing.Publish(template.Folder, fileName, content);
                result.Ok = true; result.Outcome = IvtOutcome.Succeeded;
                result.Message = "'" + fileName + "' is in the " + template.Title + " folder. It is loaded when you start the load job.";
            }
            catch (Exception ex) { result.Outcome = IvtOutcome.Failed; result.Message = IvtErrors.Describe(ex, correlation); }
            result.Warning = await audited.CompleteAsync(result.Outcome, result.Ok ? 1 : 0, null, new { file = fileName, bytes = content.Length, error = result.Ok ? null : result.Message }, cancellation).ConfigureAwait(false);
            return result;
        }

        public async Task<IvtActionResult> StartLoadAsync(IvtFileLoadKind kind, bool confirmed, IvtOperator op, CancellationToken cancellation)
        {
            var correlation = Guid.NewGuid();
            var job = JobFor(kind);
            var entry = new IvtAuditEntry { CorrelationId = correlation, Operator = op.Login, Environment = "production", Operation = "FILE.LOAD." + kind.ToString().ToUpperInvariant(), Target = job, ExternalId = job };
            if (!op.IsAdmin) return IvtActionResult.Rejected("IVT administrator permission is required.", correlation);
            if (!confirmed)
            {
                const string message = "Confirm that the correct file is in the landing folder before starting the load.";
                await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, message);
                return IvtActionResult.Rejected(message, correlation);
            }
            var kinds = kind == IvtFileLoadKind.ReferenceData ? new[] { kind } : SharedJobKinds;
            Dictionary<IvtFileLoadKind, IReadOnlyList<IvtLandingFile>> pending;
            try { pending = Pending(kinds); } catch (IvtOperationException ex) { return IvtActionResult.Rejected(ex.Message, correlation); }
            if (pending[kind].Count == 0)
            {
                var message = "There is no file waiting in the " + Template(kind).Title + " folder. Nothing was started.";
                await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, message);
                return IvtActionResult.Rejected(message, correlation);
            }
            var lockName = kind == IvtFileLoadKind.ReferenceData ? "reference-data-load" : AdminLoadLock;
            using (var lease = await rt.Coordinator.TryAcquireAsync(lockName, cancellation).ConfigureAwait(false))
            using (var engine = await rt.Coordinator.TryAcquireAsync(RunControlService.EngineLock, cancellation).ConfigureAwait(false))
            {
                if (lease == null || engine == null) return IvtActionResult.Rejected("Another load or engine start is in progress. Wait a moment and refresh.", correlation);
                var conflict = await new RunControlService(rt).CheckConflictsAsync(job, true, cancellation).ConfigureAwait(false);
                if (conflict != null) { await AuditedOperation.RecordRejectionAsync(rt.Audit, entry, conflict); return IvtActionResult.Rejected(conflict, correlation); }
                entry.Detail = Newtonsoft.Json.JsonConvert.SerializeObject(pending.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(f => f.Name)));
                var audited = await AuditedOperation.BeginAsync(rt.Audit, entry, cancellation).ConfigureAwait(false);
                var result = new IvtActionResult { CorrelationId = correlation, AuditId = audited.Entry.Id };
                try
                {
                    await rt.Agent.StartAsync(job, cancellation).ConfigureAwait(false);
                    result.Steps.Add(new IvtStep { Name = "Start " + job, Status = "Submitted" });
                    result.Ok = true; result.Outcome = IvtOutcome.Submitted;
                    result.Message = job + " was submitted. It runs in SQL Agent - refresh the status; loader errors appear below when it fails. This is not completion.";
                }
                catch (IvtOperationException ex) { result.Outcome = IvtOutcome.Failed; result.Message = ex.Message; }
                catch (System.Data.SqlClient.SqlException ex) when (ex.Number != -2) { result.Outcome = IvtOutcome.Failed; result.Message = IvtErrors.Describe(ex, correlation); }
                catch (Exception ex)
                {
                    result.Outcome = IvtOutcome.Ambiguous;
                    result.Message = "It is not certain whether " + job + " started (" + IvtErrors.Describe(ex, correlation) + "). Refresh the status before trying again.";
                }
                result.Warning = await audited.CompleteAsync(result.Outcome, pending.Values.Sum(v => v.Count), null, null, cancellation).ConfigureAwait(false);
                return result;
            }
        }
    }
}
