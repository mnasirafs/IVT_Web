using AFSDataGateway.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace AFSDataGateway.Services.Chat
{
    // Learning loop for the assistant (ML.ChatGlossary, ML.ChatFeedback; migration V011):
    //  - glossary terms (synonyms, name -> code mappings, guidance) are injected into every request;
    //    a user's own suggestions apply to their chats at once, admins approve them for everyone;
    //  - thumbs up/down + comments on answers are stored for admins to review and turn into glossary entries.
    public sealed class ChatKnowledgeService
    {
        public static readonly string[] Kinds = { "SYNONYM", "CODE", "DEFINITION", "GUIDANCE" };
        private const int MaxPromptEntries = 80, MaxPromptChars = 8000;
        private readonly IVTEntities db;
        public ChatKnowledgeService(IVTEntities db) { this.db = db; }

        // Glossary text for the prompt: approved global entries plus the user's own (not rejected) suggestions.
        // Failures return null so the assistant still works if the tables are unavailable.
        public string PromptGlossary(string login)
        {
            try
            {
                var rows = db.Database.SqlQuery<GlossaryEntry>(
                    @"SELECT TOP (@max) * FROM ML.ChatGlossary
                       WHERE (Scope = N'GLOBAL' AND Status = N'APPROVED')
                          OR (Scope = N'USER' AND OwnerLogin = @login AND Status <> N'REJECTED')
                       ORDER BY CASE WHEN Scope = N'USER' THEN 0 ELSE 1 END, Term",
                    new SqlParameter("@max", MaxPromptEntries), new SqlParameter("@login", login ?? "")).ToList();
                var text = new StringBuilder();
                foreach (var row in rows)
                {
                    var line = "- " + OneLine(row.Term) + " (" + row.Kind.ToLowerInvariant() + (row.Scope == "USER" ? ", this user's own note" : "") + "): " + OneLine(row.Meaning);
                    if (text.Length + line.Length > MaxPromptChars) break;
                    text.AppendLine(line);
                }
                return text.Length == 0 ? null : text.ToString();
            }
            catch (Exception) { return null; }
        }

        public void SaveFeedback(string login, string scopeLabel, ChatTurn turn, int rating, string comment)
        {
            db.Database.ExecuteSqlCommand(
                @"INSERT INTO ML.ChatFeedback (LoginName, ScopeLabel, Question, Answer, ToolsJson, Rating, Comment)
                  VALUES (@login, @scope, @q, @a, @tools, @rating, @comment)",
                new SqlParameter("@login", login), new SqlParameter("@scope", (object)Clip(scopeLabel, 200) ?? DBNull.Value),
                new SqlParameter("@q", Clip(turn.Question, 2000)), new SqlParameter("@a", turn.Answer ?? ""),
                new SqlParameter("@tools", (object)turn.ToolsJson ?? DBNull.Value), new SqlParameter("@rating", (short)rating),
                new SqlParameter("@comment", (object)Clip(Blank(comment), 2000) ?? DBNull.Value));
        }

        // Users suggest terms: USER scope (applied to their chats now), PENDING admin approval for everyone.
        // Admins add GLOBAL, APPROVED entries directly.
        public void AddTerm(string login, bool admin, string term, string meaning, string kind)
        {
            term = Clip(Blank(term), 100); meaning = Clip(Blank(meaning), 500);
            kind = Kinds.Contains((kind ?? "").ToUpperInvariant()) ? kind.ToUpperInvariant() : "SYNONYM";
            if (term == null || meaning == null) throw new ArgumentException("Term and meaning are required.");
            db.Database.ExecuteSqlCommand(
                @"INSERT INTO ML.ChatGlossary (Term, Meaning, Kind, Scope, OwnerLogin, Status, CreatedBy, ReviewedBy, ReviewedAt)
                  VALUES (@term, @meaning, @kind, @scope, @owner, @status, @login, @reviewer, CASE WHEN @admin = 1 THEN SYSUTCDATETIME() END)",
                new SqlParameter("@term", term), new SqlParameter("@meaning", meaning), new SqlParameter("@kind", kind),
                new SqlParameter("@scope", admin ? "GLOBAL" : "USER"), new SqlParameter("@owner", admin ? (object)DBNull.Value : login),
                new SqlParameter("@status", admin ? "APPROVED" : "PENDING"), new SqlParameter("@login", login),
                new SqlParameter("@reviewer", admin ? (object)login : DBNull.Value), new SqlParameter("@admin", admin ? 1 : 0));
        }

        public List<GlossaryEntry> MyTerms(string login)
        {
            return db.Database.SqlQuery<GlossaryEntry>(
                "SELECT * FROM ML.ChatGlossary WHERE Scope = N'USER' AND OwnerLogin = @login ORDER BY CreatedAt DESC",
                new SqlParameter("@login", login)).ToList();
        }

        // Owners can delete their own suggestions.
        public void DeleteOwnTerm(string login, int id)
        {
            db.Database.ExecuteSqlCommand("DELETE FROM ML.ChatGlossary WHERE GlossaryId = @id AND Scope = N'USER' AND OwnerLogin = @login",
                new SqlParameter("@id", id), new SqlParameter("@login", login));
        }

        // ---- Admin ----
        public List<GlossaryEntry> AllTerms()
        {
            return db.Database.SqlQuery<GlossaryEntry>(
                @"SELECT * FROM ML.ChatGlossary
                   ORDER BY CASE Status WHEN N'PENDING' THEN 0 WHEN N'APPROVED' THEN 1 ELSE 2 END, Scope, Term").ToList();
        }

        // approve: make a suggestion GLOBAL + APPROVED; reject: keep for audit but stop using it; delete: remove.
        public void ReviewTerm(int id, string decision, string admin)
        {
            string sql;
            switch ((decision ?? "").ToLowerInvariant())
            {
                case "approve": sql = "UPDATE ML.ChatGlossary SET Scope = N'GLOBAL', Status = N'APPROVED', ReviewedBy = @admin, ReviewedAt = SYSUTCDATETIME() WHERE GlossaryId = @id"; break;
                case "reject": sql = "UPDATE ML.ChatGlossary SET Status = N'REJECTED', ReviewedBy = @admin, ReviewedAt = SYSUTCDATETIME() WHERE GlossaryId = @id"; break;
                case "delete": sql = "DELETE FROM ML.ChatGlossary WHERE GlossaryId = @id"; break;
                default: throw new ArgumentException("Unknown decision.");
            }
            var parameters = new List<SqlParameter> { new SqlParameter("@id", id) };
            if (sql.Contains("@admin")) parameters.Add(new SqlParameter("@admin", admin));
            db.Database.ExecuteSqlCommand(sql, parameters.ToArray());
        }

        public List<FeedbackEntry> Feedback(string status, int max = 200)
        {
            return db.Database.SqlQuery<FeedbackEntry>(
                @"SELECT TOP (@max) * FROM ML.ChatFeedback WHERE (@status IS NULL OR Status = @status)
                   ORDER BY CASE Status WHEN N'NEW' THEN 0 ELSE 1 END, CreatedAt DESC",
                new SqlParameter("@max", max), new SqlParameter("@status", (object)Blank(status) ?? DBNull.Value)).ToList();
        }

        public void ReviewFeedback(long id, string status, string note, string admin)
        {
            if (status != "REVIEWED" && status != "ACTIONED" && status != "NEW") throw new ArgumentException("Unknown status.");
            db.Database.ExecuteSqlCommand(
                "UPDATE ML.ChatFeedback SET Status = @status, AdminNote = @note, ReviewedBy = @admin, ReviewedAt = SYSUTCDATETIME() WHERE FeedbackId = @id",
                new SqlParameter("@status", status), new SqlParameter("@note", (object)Clip(Blank(note), 1000) ?? DBNull.Value),
                new SqlParameter("@admin", admin), new SqlParameter("@id", id));
        }

        private static string Blank(string value) { return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        private static string Clip(string value, int max) { return value == null || value.Length <= max ? value : value.Substring(0, max); }
        private static string OneLine(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }
    }

    public sealed class GlossaryEntry
    {
        public int GlossaryId { get; set; }
        public string Term { get; set; }
        public string Meaning { get; set; }
        public string Kind { get; set; }
        public string Scope { get; set; }
        public string OwnerLogin { get; set; }
        public string Status { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public long? SourceFeedbackId { get; set; }
    }

    public sealed class FeedbackEntry
    {
        public long FeedbackId { get; set; }
        public string LoginName { get; set; }
        public string ScopeLabel { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public string ToolsJson { get; set; }
        public short Rating { get; set; }
        public string Comment { get; set; }
        public string Status { get; set; }
        public string AdminNote { get; set; }
        public string ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
