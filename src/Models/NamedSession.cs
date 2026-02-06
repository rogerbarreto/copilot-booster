
using System;

namespace CopilotApp.Models;

internal class NamedSession
{
    public string Id { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime LastModified { get; set; }
}
