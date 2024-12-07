using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Minitwit.Core.Entities;
using Minitwit.Core.Repository;
using Minitwit.Web.Models;

namespace Minitwit.Web.ApiControllers;

[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    private readonly IAuthorRepository _authorRepository;
    private readonly IMessageRepository _MessageRepository;
    private readonly ILatestRepository _latestRepository;
    private readonly UserManager<Author> _userManager;
    private readonly IUserStore<Author> _userStore;
    private readonly IUserEmailStore<Author> _emailStore;

    public ApiController(
        IAuthorRepository authorRepository,
        IMessageRepository MessageRepository,
        ILatestRepository latestRepository,
        UserManager<Author> userManager,
        IUserStore<Author> userStore
    )
    {
        _authorRepository = authorRepository;
        _MessageRepository = MessageRepository;
        _latestRepository = latestRepository;
        _userManager = userManager;
        _userStore = userStore;
        _emailStore = GetEmailStore();
    }

    private const string latestLogFilePath = "./LogLatestGet.txt";
    private const string registerLogFilePath = "./LogRegisterPost.txt";
    private const string msgsGetLogFilePath = "./LogMsgsGet.txt";
    private const string msgsPrivateGetLogFilePath = "./LogMsgsPrivateGet.txt";
    private const string msgsPostLogFilePath = "./LogMsgsPost.txt";
    private const string fllwsGetLogFilePath = "./LogFllwsGet.txt";
    private const string fllwsPostLogFilePath = "./LogFllwsPost.txt";
    
    private const string UnauthorizedMessage = "You are not authorized to use this resource";

    //Returns the id of the latest command read from a text file and defaults to -1
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest()
    {
        // Checks authorization
        if (NotReqFromSimulator(Request))
        {
            return StatusCode(403, UnauthorizedMessage);
        }

        try
        {
            // Fetch entry
            var latestEntry = await _latestRepository.GetLatestAsync();

            // Default to -1 if no entry is found
            var latestProcessedCommandId = latestEntry?.Value ?? -1;

            return Ok(new { latest = latestProcessedCommandId });
        }
        catch (Exception ex)
        {
            await LogRequest("{}", $"{{{ex.StackTrace}}}", latestLogFilePath);

            // Handle exception
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterUser(
        [FromQuery] int latest,
        [FromBody] RegisterUserData data
    )
    {   

        // Checks authorization
        if (NotReqFromSimulator(Request))
        {   
            
            return StatusCode(403, UnauthorizedMessage);
        }

        await Update_Latest(latest);

        var result = await CreateUser(data.username, data.email, data.pwd);

        if (result.Succeeded)
        {
            return StatusCode(204, "");
        }

        await LogRequest(
            data.ToString(),
            StringifyIdentityResultErrors(result),
            registerLogFilePath
        );
        return BadRequest($"{result.Errors.ToList()}");
    }

    [HttpGet("msgs")]
    public async Task<IActionResult> GetMessagesFromPublicTimeline(
        [FromQuery] int latest,
        [FromQuery] int no = 100
    )
    {
        // Checks authorization
        if (NotReqFromSimulator(Request))
        {

            return StatusCode(403, UnauthorizedMessage);
        }

        await Update_Latest(latest);

        if (no < 0)
            no = 100;

        try
        {
            var Messages = await _MessageRepository.GetMessagesByCountAsync(no);
            var authorIds = Messages.Select(c => c.AuthorId).Distinct();
            var users = await _authorRepository.GetAuthorsByIdAsync(authorIds);

            var lst = ConvertToMessageViewModelApiCollection(Messages, users);

            return Ok(lst);
        }
        catch (Exception ex)
        {
            await LogRequest(
                $"{{Latest = {latest}, No = {no}}}",
                $"{{{ex.StackTrace}}}",
                msgsGetLogFilePath
            );
            return NotFound();
        }
    }

    [HttpGet("msgs/{username}")]
    public async Task<IActionResult> GetUserMessages(
        [FromRoute] string username,
        [FromQuery] int latest,
        [FromQuery] int no = 100
    )
    {
        // Checks authorization
        if (NotReqFromSimulator(Request))
        {
            return StatusCode(403, UnauthorizedMessage);
        }

        await Update_Latest(latest);

        if (no < 0)
            no = 100;

        try
        {
            Author author = await _authorRepository.GetAuthorByNameAsync(username);
            if (author == null)
            {
                return NotFound($"User '{username}' not found.");
            }

            int authorId = author.Id;
            ICollection<Message> Messages = await _MessageRepository.GetMessagesFromAuthorByCountAsync(
                authorId,
                no
            );

            var formattedMessages = Messages
                .Select(c => new MessageViewModelApi(username, c.Text, c.TimeStamp))
                .ToList();

            return Ok(formattedMessages);
        }
        catch (Exception ex)
        {
            await LogRequest(
                $"{{User = {username}, Latest = {latest}, No = {no}}}",
                $"{{{ex.StackTrace}}}",
                msgsPrivateGetLogFilePath
            );

            return StatusCode(500, "An error occurred while processing the request.");
        }
    }

    [HttpPost("msgs/{username}")]
    public async Task<IActionResult> PostMessage(
        [FromRoute] string username,
        [FromQuery] int latest,
        [FromBody] MsgsData msgsdata
    )
    {

        // Checks authorization
        if (NotReqFromSimulator(Request))
        {
            return StatusCode(403, UnauthorizedMessage);
        }

        try
        {
            Author user = await _authorRepository.GetAuthorByNameAsync(username);
            CreateMessage Message = new CreateMessage(user.Id, msgsdata.content);
            await _MessageRepository.AddCreateMessageAsync(Message);
            await Update_Latest(latest);

            return StatusCode(204, "");
        }
        catch (Exception ex)
        {
            await LogRequest(msgsdata.ToString(), $"{{{ex.StackTrace}}}", msgsPostLogFilePath);

            return NotFound();
        }
    }

    [HttpGet("fllws/{username}")]
    public async Task<IActionResult> GetUserFollowers(
        [FromRoute] string username,
        [FromQuery] int latest,
        [FromQuery] int no = 100
    )
    {

        // Checks authorization
        if (NotReqFromSimulator(Request))
        {
            return StatusCode(403, UnauthorizedMessage);
        }

        await Update_Latest(latest);
        var output = new List<string>();

        try
        {
            if (await _authorRepository.GetAuthorByNameAsync(username) == null)
            {
                await CreateUser(username, $"{username}@user.com", "password");
            }
            
            Author author = await _authorRepository.GetAuthorByNameAsync(username);
            var authorFollowers = await _authorRepository.GetFollowingByIdAsync(author.Id);
            for (int i = 0; i < authorFollowers.Count; i++)
            {
                if (i > no - 1)
                    break;
                output.Add(authorFollowers.ElementAt(i).UserName);
            }
        }
        catch (Exception ex)
        {
            await SimpleLogRequest(
                $"{{User = {username}, Latest = {latest}, No = {no}}}",
                $"{{{ex.StackTrace}}}",
                fllwsGetLogFilePath
            );
            return NotFound();
        }
        
        return Ok(new { follows = output.Take(no) });
    }

    [HttpPost("fllws/{username}")]
    public async Task<IActionResult> FollowUser(
        [FromRoute] string username,
        [FromQuery] int latest,
        [FromBody] FollowData followData
    )
    {

        // Checks authorization
        if (NotReqFromSimulator(Request))
        {
            return StatusCode(403, "You are not authorized to use this resource");
        }

        await Update_Latest(latest);

        // Check if exactly one action is specified
        if (string.IsNullOrEmpty(followData.follow) && string.IsNullOrEmpty(followData.unfollow))
        {
            return BadRequest("Only one of 'follow' xor 'unfollow' should be provided.");
        }
        if (!string.IsNullOrEmpty(followData.follow) && !string.IsNullOrEmpty(followData.unfollow))
        {
            return BadRequest("Either 'follow' xor 'unfollow' should be provided.");
        }

        try
        {
            if (!string.IsNullOrEmpty(followData.follow))
            {

                if (await _authorRepository.GetAuthorByNameAsync(username) == null)
                {
                    await CreateUser(username, $"{username}@user.com", "password");
                }

                var followed = await _authorRepository.GetAuthorByNameAsync(followData.follow);
                var follower = await _authorRepository.GetAuthorByNameAsync(username);

                await _authorRepository.AddFollowAsync(follower.Id, followed.Id);

                return StatusCode(204, "");
            }

            if (!string.IsNullOrEmpty(followData.unfollow))
            {

                if (await _authorRepository.GetAuthorByNameAsync(followData.unfollow) == null)
                {
                    await CreateUser(
                        followData.unfollow,
                        $"{followData.unfollow}@user.com",
                        "password"
                    );
                }

                var followed = await _authorRepository.GetAuthorByNameAsync(followData.unfollow);
                var follower = await _authorRepository.GetAuthorByNameAsync(username);

                await _authorRepository.RemoveFollowAsync(follower.Id, followed.Id);

                return StatusCode(204, "");
            }
        }
        catch (Exception ex)
        {
            await SimpleLogRequest(
                $"User = {username}. Request body: {followData}",
                $"{{{ex.StackTrace}}}",
                fllwsPostLogFilePath
            );

            return NotFound();
        }
        return NotFound();
    }

    // Data containers

    private interface IData
    {
        public string GetData();
    }

    public class MsgsData : IData
    {
        public string content { get; set; }

        public string GetData()
        {
            return ToString();
        }

        public override string ToString()
        {
            return $"{{content: {content}}}";
        }
    }

    public class FollowData : IData
    {
        public string? follow { get; set; }
        public string? unfollow { get; set; }

        public string GetData()
        {
            return ToString();
        }

        public override string ToString()
        {
            return $"{{follow: {follow}, unfollow: {unfollow}}}";
        }
    }

    public class RegisterUserData : IData
    {
        public string username { get; set; }
        public string email { get; set; }
        public string pwd { get; set; }

        public string GetData()
        {
            return ToString();
        }

        public override string ToString()
        {
            return $"{{Username: {username}, Email: {email}, Password: {pwd}}}";
        }
    }

    // Helper methods

    private IUserEmailStore<Author> GetEmailStore()
    {
        if (!_userManager.SupportsUserEmail)
        {
            throw new NotSupportedException(
                "The default UI requires a user store with email support."
            );
        }
        return (IUserEmailStore<Author>)_userStore;
    }

    private static Author CreateUser()
    {
        try
        {
            return Activator.CreateInstance<Author>();
        }
        catch
        {
            throw new InvalidOperationException(
                $"Can't create an instance of 'Author'. "
                    + $"Ensure that 'Author' is not an abstract class and has a parameterless constructor, or alternatively "
                    + $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml"
            );
        }
    }

    private async Task<IdentityResult> CreateUser(string username, string email, string password)
    {
        var user = CreateUser();

        var setUserName = _userStore.SetUserNameAsync(user, username, CancellationToken.None);
        var setEmail = _emailStore.SetEmailAsync(user, email, CancellationToken.None);

        await Task.WhenAll(setUserName, setEmail);

        return await _userManager.CreateAsync(user, password);
    }

    private static bool NotReqFromSimulator(HttpRequest request)
    {
        return request.Headers.Authorization != "Basic c2ltdWxhdG9yOnN1cGVyX3NhZmUh";
    }

    //Writes the id of the latest command to a text file
    private async Task Update_Latest(int latestId = -1)
    {
        try
        {
            // Fetch the latest entry from the latest table
            var latestEntry = await _latestRepository.GetLatestAsync();

            if (latestEntry == null)
            {
                // If no entry exists, create a new one
                latestEntry = new Latest { Id = 1, Value = latestId }; // Assuming Id is always 1
                await _latestRepository.AddLatestAsync(latestEntry);
            }
            else
            {
                // If the entry exists, update the value
                latestEntry.Value = latestId;
                await _latestRepository.UpdateLatestAsync(latestEntry);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error occurred while updating latest id: {ex.Message}");
        }
    }

    private static async Task SimpleLogRequest(string data, string errors, string logFilePath)
    {
        // format everything
        string logtext = $"{data}\n{errors}\n\n";

        await using (StreamWriter writer = new StreamWriter(logFilePath, true))
        {
            await writer.WriteAsync(logtext);
        }
    }

    private async Task LogRequest(string data, string errors, string logFilePath)
    {
        // Stringify header
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append("{");

        foreach (var header in Request.Headers.ToList())
        {
            stringBuilder.Append($"{header.Key}: {header.Value}, ");
        }
        stringBuilder.Append("}");
        string headers = stringBuilder.ToString();

        // format everything
        string logtext = $"{headers}\n{data}\n{errors}\n\n";

        await using (StreamWriter writer = new StreamWriter(logFilePath, true))
        {
            await writer.WriteAsync(logtext);
        }
    }

    private static string StringifyIdentityResultErrors(IdentityResult result)
    {
        // Stringify Errors
        StringBuilder stringBuilderError = new StringBuilder();
        stringBuilderError.Append("{");
        foreach (var error in result.Errors.ToList())
        {
            stringBuilderError.Append($"\"{error.Description}\", ");
        }
        stringBuilderError.Append("}");
        return stringBuilderError.ToString();
    }

    private static ICollection<MessageViewModelApi> ConvertToMessageViewModelApiCollection(ICollection<Message> messages, ICollection<Author> users)
    {
        var lst = messages
                .Select(message => new MessageViewModelApi(
                    users.FirstOrDefault(a => a.Id == message.AuthorId)?.UserName ?? "Unknown",
                    message.Text,
                    message.TimeStamp
                ))
                .ToList();

        return lst;
    }

}
