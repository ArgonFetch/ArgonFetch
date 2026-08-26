namespace ArgonFetch.Application.Exceptions
{
    public class UnknownContentTypeException : System.Exception
    {
        public UnknownContentTypeException() : base("Unknown or unsupported content type.")
        {
        }

        public UnknownContentTypeException(string message) : base(message)
        {
        }

        public UnknownContentTypeException(string message, System.Exception innerException) : base(message, innerException)
        {
        }
    }
}
