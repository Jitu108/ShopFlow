namespace Identity.Domain.Exceptions;

public class DuplicateEmailException : DomainException
{
    public DuplicateEmailException(string email)
        : base($"Email '{email}' is already registered.") { }
}