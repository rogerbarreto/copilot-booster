namespace CopilotApp.Models;

using System;

class NamedSession
{
    public string Id { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTime LastModified { get; set; }
}
