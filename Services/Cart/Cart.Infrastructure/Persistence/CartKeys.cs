namespace Cart.Infrastructure.Persistence;

public static class CartKeys
{
    public static string ForUser(Guid userId) => $"cart:{userId}";
}
