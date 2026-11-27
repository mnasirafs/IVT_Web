using System;
using System.Collections.Generic;

namespace AFSDataGateway.Services.Chat
{
    [Serializable]
    public sealed class ChatConversation
    {
        public const int MaxTurns = 10;
        public string ScopeFingerprint { get; private set; }
        public string Token { get; private set; }
        // Short-term memory: the last few questions and answers, restored when the user moves between pages and
        // replayed to the model for follow-ups. Discarded whenever the access scope changes (new conversation).
        public List<ChatTurn> Turns { get; private set; }
        // At most one hold/release awaiting the user's Confirm; a new proposal replaces it.
        public ChatPendingAction Pending { get; set; }
        // Last answer and its source data, for the "Email this answer" button (kept only until the next question).
        public string LastQuestion { get; set; }
        public string LastAnswerText { get; set; }
        public string LastEvidenceJson { get; set; }

        public ChatConversation(string fingerprint)
        {
            ScopeFingerprint = fingerprint;
            Token = Guid.NewGuid().ToString("N");
            Turns = new List<ChatTurn>();
        }

        public bool Matches(ChatAccessScope scope, string token)
        {
            return ScopeFingerprint == scope.Fingerprint && Token == token;
        }

        public ChatTurn Remember(string question, string answer, string toolsJson)
        {
            var turn = new ChatTurn { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Question = question, Answer = answer, ToolsJson = toolsJson, At = DateTime.Now };
            Turns.Add(turn);
            while (Turns.Count > MaxTurns) Turns.RemoveAt(0);
            return turn;
        }

        public ChatTurn Find(string id)
        {
            return Turns.Find(t => t.Id == id);
        }
    }

    [Serializable]
    public sealed class ChatTurn
    {
        public string Id { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        // Tool names and arguments the answer used (filters only, never result data); lets follow-ups reuse the same filters.
        public string ToolsJson { get; set; }
        public DateTime At { get; set; }
        public int? Rating { get; set; }
    }
}
