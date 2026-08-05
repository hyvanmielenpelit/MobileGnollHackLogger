using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Extensions;
using Overseer.Models;
using Overseer.Services;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CryptoService _cryptoService;

    public AdminController(ApplicationDbContext dbContext, IConfiguration configuration, UserManager<ApplicationUser> userManager, CryptoService cryptoService)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _userManager = userManager;
        _cryptoService = cryptoService;
    }

    private bool CheckAdmin()
    {
        return _configuration.IsAdmin(User.Identity?.Name);
    }

    // --- Groups ---

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        if (!CheckAdmin()) return Forbid();

        var groups = await _dbContext.Groups
            .Select(g => new AdminGroupDto
            {
                Id = g.Id,
                DisplayName = g.DisplayName,
                CreatedAtUtc = g.CreatedAtUtc,
                UserCount = _dbContext.UserGroups.Count(ug => ug.GroupId == g.Id)
            })
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var displayName = request.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return BadRequest("Group name cannot be empty.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(displayName, @"^[a-zA-Z0-9 _\-]+$"))
        {
            return BadRequest("Group name contains invalid characters.");
        }

        var group = new Group
        {
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Groups.Add(group);
        await _dbContext.SaveChangesAsync();

        return Ok(new AdminGroupDto
        {
            Id = group.Id,
            DisplayName = group.DisplayName,
            CreatedAtUtc = group.CreatedAtUtc,
            UserCount = 0
        });
    }

    [HttpDelete("groups/{id}")]
    public async Task<IActionResult> DeleteGroup(long id)
    {
        if (!CheckAdmin()) return Forbid();

        var group = await _dbContext.Groups.FindAsync(id);
        if (group == null) return NotFound();

        _dbContext.Groups.Remove(group);
        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    // --- Users ---

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search)
    {
        if (!CheckAdmin()) return Forbid();

        var query = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => (u.UserName != null && u.UserName.Contains(search)) || (u.Email != null && u.Email.Contains(search)));
        }

        var users = await query.Take(50).ToListAsync();
        var dtos = new List<AdminUserDto>();

        foreach(var u in users)
        {
            var userGroups = await _dbContext.UserGroups
                .Include(ug => ug.Group)
                .Where(ug => ug.AspNetUserId == u.Id)
                .Select(ug => new AdminGroupDto { Id = ug.GroupId, DisplayName = ug.Group.DisplayName })
                .ToListAsync();

            dtos.Add(new AdminUserDto
            {
                Id = u.Id,
                UserName = u.UserName ?? "",
                Email = u.Email ?? "",
                Groups = userGroups
            });
        }

        return Ok(dtos);
    }

    [HttpPost("users/{userId}/groups")]
    public async Task<IActionResult> AssignGroupToUser(string userId, [FromBody] AssignGroupRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var exists = await _dbContext.UserGroups.AnyAsync(ug => ug.AspNetUserId == userId && ug.GroupId == request.GroupId);
        if (!exists)
        {
            _dbContext.UserGroups.Add(new UserGroup { AspNetUserId = userId, GroupId = request.GroupId });
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpDelete("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> RemoveGroupFromUser(string userId, long groupId)
    {
        if (!CheckAdmin()) return Forbid();

        var ug = await _dbContext.UserGroups.FirstOrDefaultAsync(u => u.AspNetUserId == userId && u.GroupId == groupId);
        if (ug != null)
        {
            _dbContext.UserGroups.Remove(ug);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    // --- System AI Configurations ---

    [HttpGet("systemconfigs")]
    public async Task<IActionResult> GetSystemConfigs()
    {
        if (!CheckAdmin()) return Forbid();

        var configs = await _dbContext.SystemAiApiConfigurations
            .OrderBy(c => c.OrderIndex)
            .Select(c => new SystemAiApiConfigurationDto
            {
                Id = c.Id,
                DisplayName = c.DisplayName,
                Provider = c.Provider,
                ModelId = c.ModelId,
                ThinkingLevel = c.ThinkingLevel,
                MaxInputTokens = c.MaxInputTokens,
                MaxOutputTokens = c.MaxOutputTokens,
                OrderIndex = c.OrderIndex,
                IsEnabled = c.IsEnabled,
                HasApiKey = !string.IsNullOrEmpty(c.EncryptedApiKey),
                IsSystemWide = c.IsSystemWide,
                MaxDailyRequests = c.MaxDailyRequests,
                MaxMonthlyRequests = c.MaxMonthlyRequests,
                MaxTotalRequests = c.MaxTotalRequests,
                DailyRequestsCount = c.DailyRequestsCount,
                MonthlyRequestsCount = c.MonthlyRequestsCount,
                TotalRequestsCount = c.TotalRequestsCount,
                ModelRole = c.ModelRole
            })
            .ToListAsync();

        return Ok(configs);
    }

    [HttpPost("systemconfigs")]
    public async Task<IActionResult> CreateSystemConfig([FromBody] CreateSystemAiApiConfigurationRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var orderIndex = await _dbContext.SystemAiApiConfigurations.AnyAsync() 
            ? await _dbContext.SystemAiApiConfigurations.MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var config = new SystemAiApiConfiguration
        {
            DisplayName = request.DisplayName,
            Provider = request.Provider,
            ModelId = request.ModelId,
            ThinkingLevel = request.ThinkingLevel,
            MaxInputTokens = request.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens,
            IsEnabled = request.IsEnabled,
            IsSystemWide = request.IsSystemWide,
            MaxDailyRequests = request.MaxDailyRequests,
            MaxMonthlyRequests = request.MaxMonthlyRequests,
            MaxTotalRequests = request.MaxTotalRequests,
            ModelRole = request.ModelRole,
            OrderIndex = orderIndex
        };

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            EncryptApiKey(config, request.ApiKey);
        }

        _dbContext.SystemAiApiConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        return Ok(new { id = config.Id });
    }

    [HttpPut("systemconfigs/{id}")]
    public async Task<IActionResult> UpdateSystemConfig(long id, [FromBody] UpdateSystemAiApiConfigurationRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        config.DisplayName = request.DisplayName;
        config.Provider = request.Provider;
        config.ModelId = request.ModelId;
        config.ThinkingLevel = request.ThinkingLevel;
        config.MaxInputTokens = request.MaxInputTokens;
        config.MaxOutputTokens = request.MaxOutputTokens;
        config.IsEnabled = request.IsEnabled;
        config.IsSystemWide = request.IsSystemWide;
        config.MaxDailyRequests = request.MaxDailyRequests;
        config.MaxMonthlyRequests = request.MaxMonthlyRequests;
        config.MaxTotalRequests = request.MaxTotalRequests;
        config.ModelRole = request.ModelRole;

        if (request.ApiKey != null)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                config.EncryptedApiKey = null;
                config.ApiKeyNonce = null;
                config.ApiKeyTag = null;
            }
            else
            {
                EncryptApiKey(config, request.ApiKey);
            }
        }

        await _dbContext.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetSystemConfigRateLimits(long id)
    {
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        config.DailyRequestsCount = 0;
        config.MonthlyRequestsCount = 0;
        config.TotalRequestsCount = 0;
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("systemconfigs/{id}")]
    public async Task<IActionResult> DeleteSystemConfig(long id)
    {
        if (!CheckAdmin()) return Forbid();

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(id);
        if (config == null) return NotFound();

        _dbContext.SystemAiApiConfigurations.Remove(config);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPut("systemconfigs/reorder")]
    public async Task<IActionResult> ReorderSystemConfigs([FromBody] ReorderRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var configs = await _dbContext.SystemAiApiConfigurations.ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- User System Config Assignments ---

    [HttpGet("users/{userId}/systemconfigs")]
    public async Task<IActionResult> GetUserSystemConfigs(string userId)
    {
        if (!CheckAdmin()) return Forbid();

        var assignments = await _dbContext.UserSystemAiApiConfigurations
            .Where(a => a.AspNetUserId == userId)
            .OrderBy(a => a.OrderIndex)
            .Select(a => new UserSystemAiConfigDto
            {
                Id = a.Id,
                UserId = a.AspNetUserId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = a.OrderIndex,
                MaxDailyRequests = a.MaxDailyRequests,
                MaxMonthlyRequests = a.MaxMonthlyRequests,
                MaxTotalRequests = a.MaxTotalRequests,
                DailyRequestsCount = a.DailyRequestsCount,
                MonthlyRequestsCount = a.MonthlyRequestsCount,
                TotalRequestsCount = a.TotalRequestsCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost("users/{userId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToUser(string userId, [FromBody] AssignConfigToUserRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        if (await _dbContext.UserSystemAiApiConfigurations.AnyAsync(a => a.AspNetUserId == userId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the user.");
        }

        var orderIndex = await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).AnyAsync() 
            ? await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var assignment = new UserSystemAiApiConfiguration
        {
            AspNetUserId = userId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyRequests = request.MaxDailyRequests,
            MaxMonthlyRequests = request.MaxMonthlyRequests,
            MaxTotalRequests = request.MaxTotalRequests,
            ModelRole = request.ModelRole,
            OrderIndex = orderIndex
        };

        _dbContext.UserSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new { id = assignment.Id });
    }
    
    [HttpDelete("user-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromUser(long id)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.UserSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("user-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateUserSystemConfig(long id, [FromBody] UpdateUserSystemAiConfigRequest request)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyRequests = request.MaxDailyRequests;
        assignment.MaxMonthlyRequests = request.MaxMonthlyRequests;
        assignment.MaxTotalRequests = request.MaxTotalRequests;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("user-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetUserSystemConfigRateLimits(long id)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.UserSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.DailyRequestsCount = 0;
        assignment.MonthlyRequestsCount = 0;
        assignment.TotalRequestsCount = 0;
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    [HttpPut("users/{userId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderUserSystemConfigs(string userId, [FromBody] ReorderRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var configs = await _dbContext.UserSystemAiApiConfigurations.Where(a => a.AspNetUserId == userId).ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Group System Config Assignments ---

    [HttpGet("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> GetGroupSystemConfigs(long groupId)
    {
        if (!CheckAdmin()) return Forbid();

        var assignments = await _dbContext.GroupSystemAiApiConfigurations
            .Where(a => a.GroupId == groupId)
            .OrderBy(a => a.OrderIndex)
            .Select(a => new GroupSystemAiConfigDto
            {
                Id = a.Id,
                GroupId = a.GroupId,
                SystemAiApiConfigurationId = a.SystemAiApiConfigurationId,
                IsEnabled = a.IsEnabled,
                OrderIndex = a.OrderIndex,
                MaxDailyRequests = a.MaxDailyRequests,
                MaxMonthlyRequests = a.MaxMonthlyRequests,
                MaxTotalRequests = a.MaxTotalRequests,
                DailyRequestsCount = a.DailyRequestsCount,
                MonthlyRequestsCount = a.MonthlyRequestsCount,
                TotalRequestsCount = a.TotalRequestsCount,
                ModelRole = a.ModelRole
            })
            .ToListAsync();

        return Ok(assignments);
    }

    [HttpPost("groups/{groupId}/systemconfigs")]
    public async Task<IActionResult> AssignSystemConfigToGroup(long groupId, [FromBody] AssignConfigToGroupRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        if (await _dbContext.GroupSystemAiApiConfigurations.AnyAsync(a => a.GroupId == groupId && a.SystemAiApiConfigurationId == request.SystemAiApiConfigurationId))
        {
            return BadRequest("This configuration is already assigned to the group.");
        }

        var orderIndex = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).AnyAsync() 
            ? await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).MaxAsync(c => c.OrderIndex) + 1 
            : 0;

        var assignment = new GroupSystemAiApiConfiguration
        {
            GroupId = groupId,
            SystemAiApiConfigurationId = request.SystemAiApiConfigurationId,
            IsEnabled = request.IsEnabled,
            MaxDailyRequests = request.MaxDailyRequests,
            MaxMonthlyRequests = request.MaxMonthlyRequests,
            MaxTotalRequests = request.MaxTotalRequests,
            ModelRole = request.ModelRole,
            OrderIndex = orderIndex
        };

        _dbContext.GroupSystemAiApiConfigurations.Add(assignment);
        await _dbContext.SaveChangesAsync();

        return Ok(new { id = assignment.Id });
    }

    [HttpDelete("group-systemconfigs/{id}")]
    public async Task<IActionResult> RemoveSystemConfigFromGroup(long id)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment != null)
        {
            _dbContext.GroupSystemAiApiConfigurations.Remove(assignment);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }
    
    [HttpPut("group-systemconfigs/{id}")]
    public async Task<IActionResult> UpdateGroupSystemConfig(long id, [FromBody] UpdateGroupSystemAiConfigRequest request)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.IsEnabled = request.IsEnabled;
        assignment.MaxDailyRequests = request.MaxDailyRequests;
        assignment.MaxMonthlyRequests = request.MaxMonthlyRequests;
        assignment.MaxTotalRequests = request.MaxTotalRequests;
        assignment.ModelRole = request.ModelRole;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("group-systemconfigs/{id}/reset")]
    public async Task<IActionResult> ResetGroupSystemConfigRateLimits(long id)
    {
        if (!CheckAdmin()) return Forbid();
        var assignment = await _dbContext.GroupSystemAiApiConfigurations.FindAsync(id);
        if (assignment == null) return NotFound();

        assignment.DailyRequestsCount = 0;
        assignment.MonthlyRequestsCount = 0;
        assignment.TotalRequestsCount = 0;
        
        await _dbContext.SaveChangesAsync();
        return Ok();
    }
    
    [HttpPut("groups/{groupId}/systemconfigs/reorder")]
    public async Task<IActionResult> ReorderGroupSystemConfigs(long groupId, [FromBody] ReorderRequest request)
    {
        if (!CheckAdmin()) return Forbid();

        var configs = await _dbContext.GroupSystemAiApiConfigurations.Where(a => a.GroupId == groupId).ToListAsync();
        var idDict = configs.ToDictionary(c => c.Id);

        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (idDict.TryGetValue(request.OrderedIds[i], out var config))
            {
                config.OrderIndex = i;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Errors ---

    [HttpGet("errors")]
    public async Task<IActionResult> GetErrors()
    {
        if (!CheckAdmin()) return Forbid();

        var errors = await _dbContext.SystemAiErrorLogs
            .Include(e => e.SystemAiApiConfiguration)
            .Where(e => !e.IsDismissed)
            .OrderByDescending(e => e.TimestampUtc)
            .Select(e => new SystemAiErrorLogDto
            {
                Id = e.Id,
                SystemAiApiConfigurationId = e.SystemAiApiConfigurationId,
                ConfigurationName = e.SystemAiApiConfiguration.DisplayName,
                ErrorMessage = e.ErrorMessage,
                HttpStatusCode = e.HttpStatusCode,
                TimestampUtc = e.TimestampUtc
            })
            .ToListAsync();

        return Ok(errors);
    }

    [HttpPost("errors/{id}/dismiss")]
    public async Task<IActionResult> DismissError(long id)
    {
        if (!CheckAdmin()) return Forbid();

        var error = await _dbContext.SystemAiErrorLogs.FindAsync(id);
        if (error == null) return NotFound();

        var user = await _userManager.GetUserAsync(User);

        error.IsDismissed = true;
        error.DismissedByUserId = user?.Id;
        error.DismissedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Helper Methods ---

    private void EncryptApiKey(SystemAiApiConfiguration config, string plainText)
    {
        var (ciphertext, nonce, tag) = _cryptoService.Encrypt(plainText, "SYSTEM_API_KEY");
        config.EncryptedApiKey = ciphertext;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
    }
}
