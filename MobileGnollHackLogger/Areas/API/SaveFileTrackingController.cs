using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using MobileGnollHackLogger.Data;
using System.Buffers.Text;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MobileGnollHackLogger.Areas.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class SaveFileTrackingController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly DbLogger _dbLogger;
        private ApplicationUser? _user = null;

        public SaveFileTrackingController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.SaveFileTracking;
            _dbLogger.LogSubType = RequestLogSubType.Default;
        }

        [Route("create")]
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] SaveFileTrackingCreateModel model)
        {
            SetDbLoggerInfo();

            _dbLogger.LogSubType = RequestLogSubType.CreateSaveFileTracking;
            _dbLogger.RequestData = model.GetData();

            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    var existingSft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.TimeStamp == model.TimeStamp && t.AspNetUserId == _user!.Id);
                    if(existingSft != null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(409, $"User '{model.UserName}' already has Save File Tracking entry with Time Stamp {model.TimeStamp}.");
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, "Sha256 is empty.");
                    }

                    try
                    {
                        var bytes = Convert.FromBase64String(model.Sha256);
                        if (bytes.Length != 32)
                        {
                            return await LogAndReturnErrorAsyncNonNull(400, "Sha256 is not 32 bytes long.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, "Error parsing Sha256 from Base64 string.", "Error parsing Sha256 from Base64 string. Message: " + ex.Message);
                    }

                    if (model.FileLength <= 0)
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, $"File Length {model.FileLength} must be positive.");
                    }

                    SaveFileTracking sft = new SaveFileTracking(model!.UserName!, _dbContext);
                    sft.CreatedDate = DateTime.UtcNow;
                    sft.TimeStamp = model.TimeStamp;
                    sft.Sha256 = model.Sha256;
                    sft.FileLength = model.FileLength;

                    await _dbContext.SaveFileTrackings.AddAsync(sft);
                    await _dbContext.SaveChangesAsync();
                    if (sft.Id == 0)
                    {
                        return await LogAndReturnErrorAsyncNonNull(500, "Inserted Id is 0.");
                    }

                    string? base64returnValue = null;
                    var actionResult = Encrypt(sft.Id, out base64returnValue);

                    if (actionResult != null)
                    {
                        return actionResult;
                    }

                    await LogAndSetSuccessAsync($"SaveFileTracking insertion succeeded. Inserted id={sft.Id}, encrypted '{base64returnValue}'.");
                    return Content(base64returnValue!); //OK
                }
                catch (Exception ex)
                {
                    return await LogAndReturnErrorAsyncNonNull(500, "SaveFileTracking creation to database failed.", "SaveFileTracking creation to database failed. Message: " + ex.Message);
                }
            }
            else
            {
                return result;
            }
        }

        [Route("use")]
        [HttpPost]
        public async Task<IActionResult> Use([FromForm] SaveFileTrackingUseModel model)
        {
            SetDbLoggerInfo();

            _dbLogger.LogSubType = RequestLogSubType.UseSaveFileTracking;
            _dbLogger.RequestData = model.GetData();

            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    if (model.TimeStamp <= 0)
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, "Model TimeStamp is not greater than 0.");
                    }

                    if(string.IsNullOrEmpty(model.EncryptedId))
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, "EncryptedId is empty.");
                    }

                    long? idOut = null;
                    var actionResult = Decrypt(model.EncryptedId, out idOut);
                    if (actionResult != null)
                    {
                        return actionResult;
                    }
                    long id = idOut!.Value;

                    var sft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.Id == id);
                    if (sft == null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(410, $"SaveFileTracking not found.", $"SaveFileTracking with the model Id {id} not found.");
                    }

                    if(sft.TimeStamp!= model.TimeStamp)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"Time Stamp '{model.TimeStamp}' is not correct.");
                    }

                    if(sft.UsedDate != null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(409, $"SaveFileTracking already used.", $"SaveFileTracking with Id {sft.Id} already used.");
                    }

                    if(sft.FileLength == 0)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"FileLength not set in database.");
                    }

                    if (model.FileLength <= 0)
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, $"Model FileLength {model.FileLength} is not positive.");
                    }

                    if(sft.FileLength != model.FileLength)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"File lengths do not match.", $"File lengths do not match. Model: {model.FileLength}. Database: {sft.FileLength}.");
                    }

                    if (sft.Sha256 == null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"Sha256 not set in database.");
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        return await LogAndReturnErrorAsyncNonNull(400, $"Sha256 not set in model.");
                    }

                    if (sft.Sha256 != model.Sha256)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"Sha256 hashes do not match.", $"Sha256 hashes do not match. Model: {model.Sha256}. Database: {sft.Sha256}.");
                    }

                    if (sft.AspNetUserId == null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(500, $"AspNetUserId is null in database.");
                    }

                    var aspNetUser = _dbContext.Users.FirstOrDefault(u => u.Id == sft.AspNetUserId);
                    if (aspNetUser == null)
                    {
                        return await LogAndReturnErrorAsyncNonNull(500, $"AspNetUser not found.", $"AspNetUser '{sft.AspNetUserId}' not found in database.");
                    }

                    if (aspNetUser.UserName != model.UserName)
                    {
                        return await LogAndReturnErrorAsyncNonNull(403, $"User names do not match.", $"User names do not match. Model: '{model.UserName}'. Database: '{aspNetUser.UserName}'.");
                    }

                    sft.UsedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    await LogAndSetSuccessAsync($"SaveFileTracking {sft.Id} use succeeded.");
                    return Ok();
                }
                catch (Exception ex)
                {
                    return await LogAndReturnErrorAsyncNonNull(500, "SaveFileTracking use failed in exception.", "SaveFileTracking use failed in exception. Message: " + ex.Message);
                }
            }
            else
            {
                return result;
            }
        }

        private async Task<IActionResult?> LogInAsync(LoginInfoModel model)
        {
            try
            {
                if (model == null)
                {
                    return await LogAndReturnErrorAsync(400, "All model data is missing.");
                }

                if (model.UserName == null)
                {
                    return await LogAndReturnErrorAsync(400, "UserName is missing.");
                }
                else
                {
                    _dbLogger.RequestUserName = model.UserName;
                }

                if (model.Password == null)
                {
                    return await LogAndReturnErrorAsync(400, "Password is missing.");
                }

                if (model.AntiForgeryToken == null)
                {
                    return await LogAndReturnErrorAsync(400, "AntiForgeryToken is missing.");
                }
                else
                {
                    _dbLogger.RequestAntiForgeryToken = model.AntiForgeryToken;
                }

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    return await LogAndReturnErrorAsync(401, "AntiForgeryToken is wrong.");
                }

                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    _dbLogger.LoginSucceeded = true;

                    //Sign in succeedeed
                    //Check next whether the user is not banned
                    try
                    {
                        _user = await _userManager.FindByNameAsync(model.UserName);
                        if (_user != null)
                        {
                            if (_user.IsBanned == true)
                            {
                                return await LogAndReturnErrorAsync(423, $"User '{model.UserName}' is banned.");
                            }
                        }
                        else
                        {
                            return await LogAndReturnErrorAsync(500, "Server error occurred while verifying user: User is null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        return await LogAndReturnErrorAsync(500, "Server error occurred while verifying user.", "Server error occurred while verifying user: " + ex.Message);
                    }

                    //Everything OK
                    return null;
                }
                if (result.RequiresTwoFactor)
                {
                    return await LogAndReturnErrorAsync(412, $"User '{model.UserName}' requires two factor authentication.");
                }
                if (result.IsLockedOut)
                {
                    return await LogAndReturnErrorAsync(423, $"User '{model.UserName}' requires is locked out.");
                }
                else
                {
                    return await LogAndReturnErrorAsync(403, $"Login failed for user '{model.UserName}'.");
                }
            }
            catch (Exception ex)
            {
                return await LogAndReturnErrorAsync(500, "Exception", "Exception: " + ex.Message);
            }
        }

        private IActionResult? Encrypt(long id, out string? encodedValue)
        {
            ICryptoTransform? encryptor = null;
            var actionResult = GetEncryptor(out encryptor);
            if(actionResult != null)
            {
                encodedValue = null;
                return actionResult;
            }
            else
            {
                using MemoryStream msEncrypt = new();
                using CryptoStream csEncrypt = new(msEncrypt, encryptor!, CryptoStreamMode.Write);
                using (StreamWriter swEncrypt = new(csEncrypt))
                {
                    swEncrypt.Write(id.ToString());
                }

                encodedValue = Convert.ToBase64String(msEncrypt.ToArray());
                return null;
            }
        }

        private IActionResult? Decrypt(string base64EncodedId, out long? id)
        {
            id = null;
            ICryptoTransform? decryptor = null;
            var actionResult = GetDecryptor(out decryptor);
            if (actionResult != null)
            {
                _dbLogger.DecryptionSucceeded = false;
                return actionResult;
            }
            else
            {
                string? idString = null;
                try
                {
                    var bytes = Convert.FromBase64String(base64EncodedId);

                    using MemoryStream msDecrypt = new(bytes);
                    using CryptoStream csDecrypt = new(msDecrypt, decryptor!, CryptoStreamMode.Read);
                    using StreamReader srDecrypt = new(csDecrypt);

                    idString = srDecrypt.ReadToEnd();
                }
                catch (Exception ex)
                {
                    _dbLogger.DecryptionSucceeded = false;
                    return LogAndReturnError(500, "Decryption failed. Exception", "Exception: " + ex.Message);
                }

                _dbLogger.DecryptionSucceeded = true;
                long result = 0;
                bool ok = long.TryParse(idString, out result);
                if(!ok)
                {
                    return LogAndReturnError(400, "Decrypted content is invalid.", $"Decrypted content is invalid: '{idString}'.");
                }

                id = result;
                return null;
            }
        }

        private IActionResult? GetEncryptor(out ICryptoTransform? encryptor)
        {
            encryptor = null;
            System.Security.Cryptography.Aes? aes = null;
            var actionResult = GetAes(out aes);
            if (actionResult != null)
            {
                return actionResult;
            }
            encryptor = aes!.CreateEncryptor(aes.Key, aes.IV);
            return null;
        }

        private IActionResult? GetDecryptor(out ICryptoTransform? decryptor)
        {
            decryptor = null;
            System.Security.Cryptography.Aes? aes = null;
            var actionResult = GetAes(out aes);
            if (actionResult != null)
            {
                return actionResult;
            }
            decryptor = aes!.CreateDecryptor(aes.Key, aes.IV);
            return null;
        }

        private IActionResult? GetAes(out System.Security.Cryptography.Aes? aes)
        {
            aes = null;
            var keySizeBits = 256;
            var blockSizeBits = 128;
            var encryptionKeyString = _configuration["EncryptionKeyString"];
            if (string.IsNullOrEmpty(encryptionKeyString))
            {
                return LogAndReturnError(500, "Server Error. Encryption key string not found.");
            }
            var key = Encoding.UTF8.GetBytes(encryptionKeyString);
            if (key.Length * 8 != keySizeBits)
            {
                return LogAndReturnError(500, "Server Error. Encryption key is of wrong size.");
            }

            var ivString = _configuration["EncryptionIVString"];
            if (string.IsNullOrEmpty(ivString))
            {
                return LogAndReturnError(500, "Server Error. IV string not found.");
            }
            var iv = Encoding.UTF8.GetBytes(ivString);
            if (iv.Length * 8 != blockSizeBits)
            {
                return LogAndReturnError(500, "Server Error. IV is of wrong size.");
            }

            var symmetricAlgorithm = System.Security.Cryptography.Aes.Create();
            symmetricAlgorithm.KeySize = keySizeBits;
            symmetricAlgorithm.Key = key;
            symmetricAlgorithm.BlockSize = blockSizeBits;
            symmetricAlgorithm.IV = iv;
            symmetricAlgorithm.Mode = CipherMode.CBC;
            symmetricAlgorithm.Padding = PaddingMode.PKCS7;

            aes = symmetricAlgorithm;
            return null;
        }

        private async Task<IActionResult?> LogAndReturnErrorAsync(int statusCode, string returnMessage, string? logMessage = null)
        {
            return await LogAndReturnErrorAsyncCommon(statusCode, returnMessage, logMessage);
        }

        private async Task<IActionResult> LogAndReturnErrorAsyncNonNull(int statusCode, string returnMessage, string? logMessage = null)
        {
            return await LogAndReturnErrorAsyncCommon(statusCode, returnMessage, logMessage);
        }

        private async Task<IActionResult> LogAndReturnErrorAsyncCommon(int statusCode, string returnMessage, string? logMessage)
        {
            string logMessage2 = logMessage ?? returnMessage;
            await _dbLogger.LogRequestAsync(logMessage2, Data.LogLevel.Error, statusCode);
            Response.StatusCode = statusCode;
            return Content(returnMessage);
        }

        private IActionResult? LogAndReturnError(int statusCode, string returnMessage, string? logMessage = null)
        {
            string logMessage2 = logMessage ?? returnMessage;
            _dbLogger.LogRequest(logMessage2, Data.LogLevel.Error, statusCode);
            Response.StatusCode = statusCode;
            return Content(returnMessage);
        }

        private async Task LogAndSetSuccessAsync(string logMessage)
        {
            var statusCode = 200;
            Response.StatusCode = statusCode;
            await _dbLogger.LogRequestAsync(logMessage, Data.LogLevel.Info, statusCode);
        }

        private void SetDbLoggerInfo()
        {
            _dbLogger.RequestMethod = Request.Method;
            _dbLogger.LastRequestId = Guid.NewGuid();
            _dbLogger.RequestPath = Request.GetEncodedUrl();
            _dbLogger.UserIPAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}
