using QNotch.Core;

namespace QNotch.Modules.Agents;

/// <summary>
/// Thread titles from T3 Code's database (~/.t3/userdata/statev2.sqlite, or legacy state.sqlite), keyed by the id hooks send as session_id:
/// the Claude Code session it resumes, or the Codex thread. Read through Windows' own winsqlite3, read only, so T3 keeps writing.
/// </summary>
internal static partial class T3Titles
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".t3", "userdata");

    const string Query = """
        SELECT coalesce(json_extract(r.resume_cursor_json, '$.resume'), json_extract(r.resume_cursor_json, '$.threadId')), t.title
        FROM provider_session_runtime r JOIN projection_threads t USING (thread_id)
        WHERE t.deleted_at IS NULL AND t.title <> ''
        """;

    /// <summary>Session id to title; empty without T3 Code.</summary>
    public static Dictionary<string, string> Read()
    {
        var names = new Dictionary<string, string>();
        var current = Path.Combine(DataDir, "statev2.sqlite");
        var query = File.Exists(current) ? """
            SELECT json_extract(p.payload_json, '$.nativeThreadRef.nativeId'), t.title
            FROM orchestration_v2_projection_provider_threads p
            JOIN orchestration_v2_projection_threads t USING (thread_id)
            WHERE t.deleted_at IS NULL AND t.title <> ''
            """ : Query;
        var path = File.Exists(current) ? current : Path.Combine(DataDir, "state.sqlite");
        foreach (var row in LocalSqlite.ReadRows(path, query))
            if (row[0].Length > 0) names[row[0]] = row[1].Trim();
        return names;
    }

}
