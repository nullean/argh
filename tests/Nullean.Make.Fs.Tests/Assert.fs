/// The handful of xUnit-style assertions these tests use, on AwesomeAssertions.
/// F# can not pick between AwesomeAssertions' many Should() overloads for a generic value, so the tests call these
/// instead; each keeps the meaning of the xUnit assertion it replaced.
namespace Nullean.Make.Fs.Tests

open System.Collections.Generic
open System.Runtime.CompilerServices
open AwesomeAssertions

// AwesomeAssertions names the subject of a failure by reading the source line that called Should(). Not inlining
// these keeps that line in this file, where it names the subject, instead of an F# test line it can not parse.
[<AbstractClass; Sealed>]
type Assert =
    /// Like xUnit's Assert.Equal: Equals, which for F# lists and records is structural
    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Equal<'T>(expected: 'T, actual: 'T) =
        let actual = box actual
        actual.Should().Be(box expected) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Contains(expectedSubstring: string, actual: string) =
        actual.Should().Contain(expectedSubstring) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Contains<'T>(expected: 'T, collection: IEnumerable<'T>) =
        collection.Should().Contain(expected) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member DoesNotContain(expectedSubstring: string, actual: string) =
        actual.Should().NotContain(expectedSubstring) |> ignore

    [<MethodImpl(MethodImplOptions.NoInlining)>]
    static member Empty<'T>(collection: IEnumerable<'T>) =
        collection.Should().BeEmpty() |> ignore
