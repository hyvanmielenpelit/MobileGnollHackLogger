using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using MobileGnollHackLogger.Data;
using System.Security.Cryptography;
using System.Text;

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
        private ApplicationUser? _user = null;

        public SaveFileTrackingController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        [Route("create")]
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] SaveFileTrackingCreateModel model)
        {
            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    var existingSft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.TimeStamp == model.TimeStamp && t.AspNetUserId == _user!.Id);
                    if(existingSft != null)
                    {
                        Response.StatusCode = 409; //Conflict
                        return Content($"User '{model.UserName}' already has Save File Tracking entry with Time Stamp {model.TimeStamp}.");
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        Response.StatusCode = 400;
                        return Content($"Sha256 is empty.");
                    }

                    try
                    {
                        var bytes = Convert.FromBase64String(model.Sha256);
                        if (bytes.Length != 32)
                        {
                            Response.StatusCode = 400;
                            return Content($"Sha256 is not 32 bytes long.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.StatusCode = 400;
                        return Content($"Error parsing Sha256 from Base64 string. Message: " + ex.Message);
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        Response.StatusCode = 400;
                        return Content($"Sha256 is empty.");
                    }

                    try
                    {
                        var bytes = Convert.FromBase64String(model.Sha256);
                        if (bytes.Length != 32)
                        {
                            Response.StatusCode = 400;
                            return Content($"Sha256 is not 32 bytes long.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.StatusCode = 400;
                        return Content($"Error parsing Sha256 from Base64 string. Message: " + ex.Message);
                    }

                    if (model.FileLength <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"File Length {model.FileLength} must be positive.");
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
                        Response.StatusCode = 500;
                        return Content("Inserted Id is 0.");
                    }

                    string? base64returnValue = null;
                    var actionResult = Encrypt(sft.Id, out base64returnValue);

                    if (actionResult != null)
                    {
                        return actionResult;
                    }

                    return Content(base64returnValue!); //OK
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("SaveFileTracking creation to database failed. Message: " + ex.Message);
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
            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    if (model.TimeStamp <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model TimeStamp is not greater than 0.");
                    }

                    if(string.IsNullOrEmpty(model.EncryptedId))
                    {
                        Response.StatusCode = 400;
                        return Content($"EncryptedId is empty.");
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
                        Response.StatusCode = 404;
                        return Content($"SaveFileTracking with the model Id not found.");
                    }

                    if(sft.TimeStamp!= model.TimeStamp)
                    {
                        Response.StatusCode = 403;
                        return Content($"UniqueId {model.TimeStamp} is not correct.");
                    }

                    if(sft.UsedDate != null)
                    {
                        Response.StatusCode = 410;
                        return Content($"SaveFileTracking with TimeStamp {model.TimeStamp} and Id {sft.Id} already used.");
                    }

                    if(sft.FileLength == 0)
                    {
                        Response.StatusCode = 403;
                        return Content($"FileLength not set in database.");
                    }

                    if (model.FileLength <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model FileLength {model.FileLength} is not positive.");
                    }

                    if(sft.FileLength != model.FileLength)
                    {
                        Response.StatusCode = 403;
                        return Content($"File lengths do not match.");
                    }

                    if (sft.Sha256 == null)
                    {
                        Response.StatusCode = 403;
                        return Content($"Sha256 not set in database.");
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        Response.StatusCode = 400;
                        return Content($"Sha256 not set in Model.");
                    }

                    if (sft.Sha256 != model.Sha256)
                    {
                        Response.StatusCode = 403;
                        return Content($"Sha256 hashes do not match.");
                    }

                    if(sft.AspNetUserId == null)
                    {
                        Response.StatusCode = 500;
                        return Content($"AspNetUserId is null.");
                    }

                    var aspNetUser = _dbContext.Users.FirstOrDefault(u => u.Id == sft.AspNetUserId);
                    if (aspNetUser == null)
                    {
                        Response.StatusCode = 500;
                        return Content($"AspNetUser not found.");
                    }

                    if (aspNetUser.UserName != model.UserName)
                    {
                        Response.StatusCode = 403;
                        return Content($"User names do not match.");
                    }

                    sft.UsedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return Ok();
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("SaveFileTracking use failed. Message: " + ex.Message);
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
                    Response.StatusCode = 400; //Bad Request
                    return Content("All model data is missing."); //Bad Request
                }

                if (model.UserName == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("UserName is missing.");
                }

                if (model.Password == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("Password is missing.");
                }
                if (model.AntiForgeryToken == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("AntiForgeryToken is missing.");
                }

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    Response.StatusCode = 401; //Not Authorized
                    return Content("AntiForgeryToken is wrong.");
                }

                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    //Sign in succeedeed
                    //Check next whether the user is not banned
                    try
                    {
                        _user = await _userManager.FindByNameAsync(model.UserName);
                        if (_user != null)
                        {
                            if (_user.IsBanned == true)
                            {
                                Response.StatusCode = 423; //Server Error
                                return Content("User " + model.UserName + " is banned.");
                            }
                        }
                        else
                        {
                            Response.StatusCode = 500; //Server Error
                            return Content("Server error occurred while verifying user: User is null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.StatusCode = 500; //Server Error
                        return Content("Server error occurred while verifying user: " + ex.Message);
                    }

                    //Everything OK
                    return null;
                }
                if (result.RequiresTwoFactor)
                {
                    Response.StatusCode = 412;
                    return Content($"User '{model.UserName}' requires two factor authentication.");
                }
                if (result.IsLockedOut)
                {
                    Response.StatusCode = 423;
                    return Content($"User '{model.UserName}' requires is locked out.");
                }
                else
                {
                    Response.StatusCode = 403;
                    return Content($"Login failed for user '{model.UserName}'.");
                }
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Content("Exception: " + ex.Message);
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
                return actionResult;
            }
            else
            {
                var bytes = Convert.FromBase64String(base64EncodedId);

                string? idString = null;
                try
                {
                    using MemoryStream msDecrypt = new(bytes);
                    using CryptoStream csDecrypt = new(msDecrypt, decryptor!, CryptoStreamMode.Read);
                    using StreamReader srDecrypt = new(csDecrypt);

                    idString = srDecrypt.ReadToEnd();
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500;
                    return Content("Server Error in Decryption. Message: " + ex.Message);
                }

                long result = 0;
                bool ok = long.TryParse(idString, out result);
                if(!ok)
                {
                    Response.StatusCode = 400;
                    return Content("Decrypted content is invalid.");
                }

                id = result;
                return null;
            }
        }

        private IActionResult? GetEncryptor(out ICryptoTransform? encryptor)
        {
            encryptor = null;
            Aes? aes = null;
            var actionResult = GetAes(out aes);
            if (actionResult != null)
            {
                return actionResult;
            }
            encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            return null;
        }

        private IActionResult? GetDecryptor(out ICryptoTransform? decryptor)
        {
            decryptor = null;
            Aes? aes = null;
            var actionResult = GetAes(out aes);
            if (actionResult != null)
            {
                return actionResult;
            }
            decryptor = aes!.CreateDecryptor(aes.Key, aes.IV);
            return null;
        }

        private IActionResult? GetAes(out Aes? aes)
        {
            aes = null;
            var keySizeBits = 256;
            var blockSizeBits = 128;
            var encryptionKeyString = _configuration["EncryptionKeyString"];
            if (string.IsNullOrEmpty(encryptionKeyString))
            {
                Response.StatusCode = 500;
                return Content("Server Error. Encryption key string not found.");
            }
            var key = Encoding.UTF8.GetBytes(encryptionKeyString);
            if (key.Length * 8 != keySizeBits)
            {
                Response.StatusCode = 500;
                return Content("Server Error. Encryption key is of wrong size.");
            }

            var ivString = _configuration["EncryptionIVString"];
            if (string.IsNullOrEmpty(ivString))
            {
                Response.StatusCode = 500;
                return Content("Server Error. IV string not found.");
            }
            var iv = Encoding.UTF8.GetBytes(ivString);
            if (iv.Length * 8 != blockSizeBits)
            {
                Response.StatusCode = 500;
                return Content("Server Error. IV is of wrong size.");
            }

            var symmetricAlgorithm = Aes.Create();
            symmetricAlgorithm.KeySize = keySizeBits;
            symmetricAlgorithm.Key = key;
            symmetricAlgorithm.BlockSize = blockSizeBits;
            symmetricAlgorithm.IV = iv;
            symmetricAlgorithm.Mode = CipherMode.CBC;
            symmetricAlgorithm.Padding = PaddingMode.PKCS7;

            aes = symmetricAlgorithm;
            return null;
        }
    }
}
