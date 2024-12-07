﻿using Minitwit.Core.Entities;

namespace Minitwit.Core.Repository;

public interface IMessageRepository
{
    public Task<ICollection<Message>> GetMessagesByPageAsync(int page);

    public Task<ICollection<Message>> GetMessagesByCountAsync(int count);

    public Task<ICollection<Message>> GetMessagesFromAuthorByCountAsync(int authorId, int count);

    public Task AddMessageAsync(Message messageDto);
    public Task<Message> AddCreateMessageAsync(CreateMessage message);
    public Task<int> GetMessageCountAsync();
    public Task<int> GetPageCountAsync();
}
