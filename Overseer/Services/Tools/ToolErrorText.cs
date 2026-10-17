using System;
using System.Text.Json;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// The text a tool failure shows the model. Exceptions this codebase throws for problems the
    /// model can correct keep their message; anything else is reduced to its type name, so a path,
    /// connection string or stack detail in an unexpected exception never reaches the model.
    /// </summary>
    internal static class ToolErrorText
    {
        public static string ForModel(Exception ex)
        {
            return ex switch
            {
                ArgumentException or FormatException or JsonException or NotSupportedException => ex.Message,
                _ => $"An internal error occurred while running the tool ({ex.GetType().Name}). It has been logged."
            };
        }
    }
}
