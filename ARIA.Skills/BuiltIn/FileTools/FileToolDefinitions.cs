using ARIA.Core.Models;

namespace ARIA.Skills.BuiltIn.FileTools;

public static class FileToolDefinitions
{
    public const string ReadFile = "read_file";
    public const string WriteFile = "write_file";
    public const string AppendFile = "append_file";
    public const string ListDirectory = "list_directory";
    public const string CreateDirectory = "create_directory";
    public const string DeleteFile = "delete_file";
    public const string MoveFile = "move_file";
    public const string FileExists = "file_exists";

    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new(ReadFile, "Read a UTF-8 text file from the workspace.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative file path." }
              },
              "required": ["path"]
            }
            """)),
        new(WriteFile, "Write a UTF-8 text file in the workspace, replacing existing content.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative file path." },
                "content": { "type": "string", "description": "Complete file content." }
              },
              "required": ["path", "content"]
            }
            """)),
        new(AppendFile, "Append UTF-8 text to a file in the workspace.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative file path." },
                "content": { "type": "string", "description": "Text to append." }
              },
              "required": ["path", "content"]
            }
            """)),
        new(ListDirectory, "List files and directories under a workspace directory.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative directory path. Use '.' for the workspace root." }
              },
              "required": ["path"]
            }
            """)),
        new(CreateDirectory, "Create a directory under the workspace.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative directory path." }
              },
              "required": ["path"]
            }
            """)),
        new(DeleteFile, "Delete a file from the workspace.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative file path." }
              },
              "required": ["path"]
            }
            """)),
        new(MoveFile, "Move or rename a file or directory inside the workspace.", Schema("""
            {
              "type": "object",
              "properties": {
                "source_path": { "type": "string", "description": "Workspace-relative source path." },
                "destination_path": { "type": "string", "description": "Workspace-relative destination path." },
                "overwrite": { "type": "boolean", "description": "Whether to replace an existing destination file." }
              },
              "required": ["source_path", "destination_path"]
            }
            """)),
        new(FileExists, "Check whether a workspace file or directory exists.", Schema("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative path." }
              },
              "required": ["path"]
            }
            """))
    ];

    private static string Schema(string json) => json;
}
