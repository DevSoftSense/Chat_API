namespace Chat.Domain.Exceptions.Messages;

public sealed class ChatOperationException : Exception
{
    public int StatusCode { get; }

    public ChatOperationException(string message, int statusCode = 400)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
