namespace PaqAgent.PedidosVenta;

/// <summary>Port de EncriptacionTalonarios.php — correlativo GVA43.PROXIMO.</summary>
public static class EncriptacionTalonarios
{
    public static (string nroPedido, string proximoEncriptadoSiguiente, int proximoNumerico) AsignarNumero(
        string proximoEncriptado,
        string tipo,
        string sucursal)
    {
        var proximo = UnCrypNro(proximoEncriptado);
        var nroPedido = CrearNumero(tipo, sucursal, proximo);
        var siguiente = proximo + 1;
        if (siguiente > 99_999_999)
        {
            throw new InvalidOperationException("Se agoto el correlativo del talonario (max. 8 digitos).");
        }

        return (nroPedido, CrypNro(siguiente), proximo);
    }

    public static string CrearNumero(string tipo, string sucursal, int proximo)
    {
        var letra = string.IsNullOrWhiteSpace(tipo) ? " " : tipo.Trim()[..1];
        var suc = (sucursal ?? string.Empty).TrimEnd();
        if (suc.Length > 5)
        {
            suc = suc[..5];
        }

        suc = suc.PadLeft(5, '0');
        var numero = proximo.ToString().PadLeft(8, '0');
        if (numero.Length > 8)
        {
            throw new InvalidOperationException("El numero de correlativo supera 8 digitos.");
        }

        return letra + suc + numero;
    }

    public static string CrypNro(int nNroDec)
    {
        var functionReturnValue = "6" + HexDigit(((nNroDec % 10) / 1) ^ 5);
        functionReturnValue = "0" + HexDigit(((nNroDec % 100) / 10) ^ 7) + functionReturnValue;
        functionReturnValue = "7" + HexDigit(((nNroDec % 1000) / 100) ^ 14) + functionReturnValue;
        functionReturnValue = "5" + HexDigit(((nNroDec % 10000) / 1000) ^ 1) + functionReturnValue;
        functionReturnValue = "7" + HexDigit(((nNroDec % 100000) / 10000) ^ 11) + functionReturnValue;
        functionReturnValue = "4" + HexDigit(((nNroDec % 1000000) / 100000) ^ 3) + functionReturnValue;
        functionReturnValue = "0" + HexDigit(((nNroDec % 10000000) / 1000000) ^ 7) + functionReturnValue;
        functionReturnValue = "7" + HexDigit(((nNroDec % 100000000) / 10000000) ^ 4) + functionReturnValue;
        return functionReturnValue;
    }

    public static int UnCrypNro(string cNroHex)
    {
        cNroHex = cNroHex.TrimEnd();
        if (cNroHex.Length < 16)
        {
            throw new InvalidOperationException("PROXIMO encriptado invalido (se esperan 16 caracteres).");
        }

        var value = HexN1(cNroHex[15]) ^ 5;
        value += (HexN1(cNroHex[13]) ^ 7) * 10;
        value += (HexN1(cNroHex[11]) ^ 14) * 100;
        value += (HexN1(cNroHex[9]) ^ 1) * 1000;
        value += (HexN1(cNroHex[7]) ^ 11) * 10000;
        value += (HexN1(cNroHex[5]) ^ 3) * 100000;
        value += (HexN1(cNroHex[3]) ^ 7) * 1000000;
        value += (HexN1(cNroHex[1]) ^ 4) * 10000000;
        return value;
    }

    private static int HexN1(char cCharHexa)
    {
        cCharHexa = char.ToUpperInvariant(cCharHexa);
        if (char.IsDigit(cCharHexa))
        {
            return cCharHexa - '0';
        }

        if (cCharHexa is >= 'A' and <= 'F')
        {
            return cCharHexa - 55;
        }

        return -1;
    }

    private static string HexDigit(int value) =>
        (value & 0xF).ToString("X");
}
