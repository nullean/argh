using System.Runtime.CompilerServices;

// Friend: Nullean.Argh.Hosting (nested DI registration). Same keypair as Hosting (build/keys/keypair.snk).
// If the signing key changes, replace PublicKey using: sn -Tp <path-to-signed-Nullean.Argh.Core.dll>
[assembly: InternalsVisibleTo(
	"Nullean.Argh.Hosting, PublicKey=002400000480000094000000060200000024000052534131000400000100010025d3a22bf3781ba85067374ad832dfcba3c4fa8dd89227e36121ba17b2c33ad6b6ce03e45e562050a031e2ff7fe12cff9060a50acbc6a0eef9ef32dc258d90f874b2e76b581938071ccc4b4d98204d1d6ca7a1988d7a211f9fc98efd808cf85f61675b11007d0eb0461dc86a968d6af8ebba7e6b540303b54f1c1f5325c252be")]
