using ARIA.Core.Models;

namespace ARIA.Skills.BuiltIn.CreateScheduledJob;

public static class CreateScheduledJobDefinitions
{
    public const string CreateScheduledJob = "create_scheduled_job";

    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new(CreateScheduledJob, "Create or update a scheduled job JSON file in the scheduler jobs directory, then reload the scheduler.", Schema("""
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Human-readable job name, for example Daily Briefing or test2."
                },
                "schedule": {
                  "type": "object",
                  "description": "Cron schedule definition.",
                  "properties": {
                    "kind": {
                      "type": "string",
                      "description": "Schedule kind. For M7 this must be cron.",
                      "enum": ["cron"]
                    },
                    "expr": {
                      "type": "string",
                      "description": "Five-field cron expression, for example 0 10 * * * for every day at 10:00."
                    },
                    "tz": {
                      "type": "string",
                      "description": "IANA time zone, for example America/New_York."
                    }
                  },
                  "required": ["kind", "expr", "tz"]
                },
                "payload": {
                  "type": "object",
                  "description": "Scheduled action payload.",
                  "properties": {
                    "kind": {
                      "type": "string",
                      "description": "Payload kind. For M7 this should be agentTurn.",
                      "enum": ["agentTurn"]
                    },
                    "message": {
                      "type": "string",
                      "description": "The exact message to send to the model when the job fires."
                    }
                  },
                  "required": ["kind", "message"]
                },
                "sessionTarget": {
                  "type": "string",
                  "description": "Use isolated unless the user explicitly asks to run in the active session.",
                  "enum": ["isolated", "main"]
                },
                "enabled": {
                  "type": "boolean",
                  "description": "Whether the job should run."
                },
                "overwrite": {
                  "type": "boolean",
                  "description": "Whether to replace an existing job file with the same kebab-case name."
                }
              },
              "required": ["name", "schedule", "payload"]
            }
            """))
    ];

    private static string Schema(string json) => json;
}
