using ARIA.Core.Models;

namespace ARIA.Skills.BuiltIn.ContextFileTools;

public static class ContextFileToolDefinitions
{
    public const string ReadContextFile = "read_context_file";
    public const string WriteContextFile = "write_context_file";

    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new(ReadContextFile, "Read a context Markdown file from workspace/context.", """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Context-directory-relative path, such as IDENTITY.md, SOUL.md, USER.md, or HEARTBEAT.md." }
              },
              "required": ["path"]
            }
            """),
        new(WriteContextFile, "Write a context Markdown file under workspace/context.", """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Context-directory-relative path, such as IDENTITY.md, SOUL.md, USER.md, or HEARTBEAT.md." },
                "content": { "type": "string", "description": "Complete Markdown content." }
              },
              "required": ["path", "content"]
            }
            """)
    ];
}
