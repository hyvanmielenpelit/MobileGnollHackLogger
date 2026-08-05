namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class Group
{
    public long Id { get; set; }

    [MaxLength(256)]
    public string DisplayName { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserGroup>? UserGroups { get; set; }
    public ICollection<GroupSystemAiApiConfiguration>? GroupSystemAiApiConfigurations { get; set; }
}
