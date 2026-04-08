using Nullean.Argh;

var app = new ArghApp();
app.Add("hello", Handlers.Hello);
return await ArghGenerated.RunAsync(args);

/// <summary>Says hello to someone.</summary>
internal static class Handlers
{
	public static void Hello(string name) => Console.WriteLine($"Hello, {name}!");
}
