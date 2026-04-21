using System.ComponentModel.DataAnnotations;
using Nullean.Argh;

namespace Nullean.Argh.Tests.Fixtures;

/// <summary>DTO with validation annotations for test coverage.</summary>
/// <param name="Count">Item count.</param>
internal sealed record ValidatedDtoArgs([Range(1, 100)] int Count);

internal static class ValidationCliHandlers
{
	/// <summary>Validate numeric range on --port.</summary>
	/// <param name="port">port the client is listening on.</param>
	public static void ValidateRange(TestGlobalCliOptions g, [Range(1, 65535)] int? port) =>
		Console.Out.WriteLine($"port:{port}");

	/// <summary>Validate string length on --name.</summary>
	public static void ValidateLength(TestGlobalCliOptions g, [StringLength(100, MinimumLength = 2)] string name) =>
		Console.Out.WriteLine($"name:{name}");

	/// <summary>Validate regex pattern on --slug.</summary>
	public static void ValidateRegex(TestGlobalCliOptions g, [RegularExpression(@"^[a-z0-9\-]+$")] string slug) =>
		Console.Out.WriteLine($"slug:{slug}");

	/// <summary>Validate allowed values on --env.</summary>
	public static void ValidateAllowed(TestGlobalCliOptions g, [AllowedValues("dev", "staging", "prod")] string env) =>
		Console.Out.WriteLine($"env:{env}");

	/// <summary>Validate email format on --address.</summary>
	public static void ValidateEmail(TestGlobalCliOptions g, [EmailAddress] string address) =>
		Console.Out.WriteLine($"email:{address}");

	/// <summary>Validate URI scheme restriction on --endpoint.</summary>
	public static void ValidateUriScheme(TestGlobalCliOptions g, [UriScheme("https")] Uri endpoint) =>
		Console.Out.WriteLine($"scheme:{endpoint.Scheme}");

	/// <summary>Validate numeric range on non-nullable --page-per with default.</summary>
	/// <param name="pagePer">Items per page.</param>
	[NoOptionsInjection]
	public static void ValidateNonNullableRange([Range(0, int.MaxValue)] int pagePer = 20) =>
		Console.Out.WriteLine($"page-per:{pagePer}");

	/// <summary>Validate DTO fields with range constraint.</summary>
	[NoOptionsInjection]
	public static void ValidateDto([AsParameters] ValidatedDtoArgs args) =>
		Console.Out.WriteLine($"dto:{args.Count}");

	/// <summary>Validate TimeSpan inclusive range.</summary>
	public static void ValidateTimeSpanRange(TestGlobalCliOptions g, [TimeSpanRange("5m", "2h")] TimeSpan window) =>
		Console.Out.WriteLine($"ts-range:{window.TotalMinutes}");
}
