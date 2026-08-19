namespace Product.Application;

public static class CacheKeys
{
    public const string Catalog = "product:catalog";

    public static string Product(Guid id) => $"product:{id}";
}
