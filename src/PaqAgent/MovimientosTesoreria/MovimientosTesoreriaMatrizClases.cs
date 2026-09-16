namespace PaqAgent.MovimientosTesoreria;

/// <summary>
/// Matriz clase × rol × D_H × TIPO (+ CC_CA) — espejo PHP MovimientosTesoreriaMatrizClases.
/// </summary>
public static class MovimientosTesoreriaMatrizClases
{
    public const string RolPrincipal = "principal";
    public const string RolFondos = "fondos";
    public const string RolOpcional = "opcional";

    private sealed record Regla(string DH, IReadOnlyList<string> Tipos, IReadOnlyList<string>? CcCa = null);

    private static readonly Dictionary<int, Dictionary<string, IReadOnlyList<Regla>>> Reglas = BuildReglas();

    public static bool ClaseSoportada(int clase) => Reglas.ContainsKey(clase);

    public static bool Admite(int clase, string rol, string dH, string tipo, string? ccCa)
    {
        if (!Reglas.TryGetValue(clase, out var roles)
            || !roles.TryGetValue(rol, out var reglas))
        {
            return false;
        }

        var tipoNorm = (tipo ?? string.Empty).Trim().ToUpperInvariant();
        var ccCaNorm = string.IsNullOrWhiteSpace(ccCa) ? null : ccCa.Trim().ToUpperInvariant();
        var dHNorm = (dH ?? string.Empty).Trim().ToUpperInvariant();

        if (rol == RolOpcional && tipoNorm == "B" && ccCaNorm == "D")
        {
            return false;
        }

        foreach (var regla in reglas)
        {
            if (regla.DH != dHNorm)
            {
                continue;
            }

            if (!regla.Tipos.Contains(tipoNorm, StringComparer.Ordinal))
            {
                continue;
            }

            if (tipoNorm == "B" && regla.CcCa is not null)
            {
                if (ccCaNorm is null || !regla.CcCa.Contains(ccCaNorm, StringComparer.Ordinal))
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    private static Dictionary<int, Dictionary<string, IReadOnlyList<Regla>>> BuildReglas() =>
        new()
        {
            [1] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] = [new("H", ["O"])],
                [RolFondos] =
                [
                    new("D", ["B"], ["C", "A"]),
                    new("D", ["C", "T", "O"])
                ],
                [RolOpcional] = [new("H", ["O"])]
            },
            [2] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] = [new("D", ["O"])],
                [RolFondos] =
                [
                    new("H", ["B"], ["C", "A", "D"]),
                    new("H", ["C", "T", "O"])
                ],
                [RolOpcional] = [new("D", ["O"])]
            },
            [3] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] =
                [
                    new("D", ["B"], ["C", "A"]),
                    new("D", ["O"])
                ],
                [RolFondos] = [new("H", ["C", "T", "O"])]
            },
            [4] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] =
                [
                    new("D", ["B", "C", "O"]),
                    new("H", ["B", "C", "O"])
                ],
                [RolFondos] =
                [
                    new("D", ["B", "C", "O"]),
                    new("H", ["B", "C", "O"])
                ]
            },
            [5] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] = [new("D", ["B"], ["C"])],
                [RolFondos] = [new("H", ["O"])],
                [RolOpcional] =
                [
                    new("D", ["O"]),
                    new("D", ["B"]),
                    new("H", ["B"])
                ]
            },
            [6] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] =
                [
                    new("H", ["B"], ["C", "A"]),
                    new("H", ["O"])
                ],
                [RolFondos] = [new("D", ["O"])],
                [RolOpcional] =
                [
                    new("H", ["O"]),
                    new("D", ["B"]),
                    new("H", ["B"])
                ]
            },
            [7] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] =
                [
                    new("D", ["O"]),
                    new("H", ["O"])
                ],
                [RolFondos] =
                [
                    new("D", ["O"]),
                    new("H", ["O"])
                ]
            },
            [8] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] = [new("D", ["B"], ["D"])],
                [RolFondos] = [new("H", ["B"], ["C"])]
            },
            [9] = new(StringComparer.Ordinal)
            {
                [RolPrincipal] =
                [
                    new("D", ["C"]),
                    new("H", ["C"])
                ],
                [RolFondos] =
                [
                    new("D", ["C"]),
                    new("H", ["C"])
                ]
            }
        };
}
