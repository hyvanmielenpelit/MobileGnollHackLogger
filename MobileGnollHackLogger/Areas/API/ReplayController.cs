using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Areas.API
{
    [Route("api/replay")]
    [ApiController]
    public class ReplayController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LogModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly DbLogger _dbLogger;
        private readonly string _replayBasePath = "";

        public ReplayController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            ILogger<LogModel> logger, IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.Bones;
            _dbLogger.LogSubType = RequestLogSubType.Default;
            _replayBasePath = _configuration["ReplayPath"] ?? "";

            if (string.IsNullOrEmpty(_replayBasePath))
            {
                throw new Exception("ReplayPath is null");
            }
        }

        [HttpGet]
        public async Task<IEnumerable<Replay>> Get()
        {
            return await _dbContext.Replay.ToArrayAsync();
        }

        [HttpGet("{id}")]
        public async Task<Replay?> Get(int id)
        {
            return await _dbContext.Replay.FirstOrDefaultAsync(r => r.Id == id);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromForm] ReplayModel model)
        {
            throw new NotImplementedException();
        }

        [HttpDelete("{id}")]
        public async Task Delete(int id)
        {
            var replay = await _dbContext.Replay.FirstOrDefaultAsync(r => r.Id == id);
            if(replay != null)
            {
                _dbContext.Replay.Remove(replay);
                int result = await _dbContext.SaveChangesAsync();
            }
            else
            {
                Response.StatusCode = 410; // Gone
            }
        }
    }
}
