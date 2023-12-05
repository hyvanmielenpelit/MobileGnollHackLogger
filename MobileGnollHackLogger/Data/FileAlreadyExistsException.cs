namespace MobileGnollHackLogger.Data
{
    public class FileAlreadyExistsException : Exception
    {
        public FileAlreadyExistsException()
        { 
            
        }

        public FileAlreadyExistsException(string? message) : base(message)
        {
            
        }
    }
}
