namespace TwoG.Connector.Core;

/// <summary>
/// Conversões entre os valores do canal (Hz, Pa, código decimal) e os formatos que o
/// SimConnect espera ou devolve (spec 02, "Mapeamento para o SimConnect").
/// </summary>
public static class RadioConversions
{
    /// <summary>Código de transponder para BCD16: 7700 → 0x7700 (XPNDR_SET).</summary>
    public static uint CodeToBcd16(long code) => DecimalToBcd(code, digits: 4);

    /// <summary>TRANSPONDER CODE em BCO16 para código decimal: 0x7700 → 7700.</summary>
    public static int Bco16ToCode(uint bco)
    {
        var code = 0;
        var scale = 1;
        for (var i = 0; i < 4; i++)
        {
            code += (int)((bco >> (4 * i)) & 0xF) * scale;
            scale *= 10;
        }
        return code;
    }

    /// <summary>
    /// Hz em BCD32 para o ADF_COMPLETE_SET (unidade Frequency ADF BCD32): quatro dígitos
    /// de kHz, o décimo e três nibbles zero, ou seja, BCD de Hz × 10.
    /// 350.000 Hz → 0x03500000; 1.234,5 kHz → 0x12345000. A confirmação em simulador
    /// é o item V4 do spec 02.
    /// </summary>
    public static uint HzToBcd32(long hz) => DecimalToBcd(hz * 10, digits: 8);

    /// <summary>Pa para milibares × 16 (KOHLSMAN_SET): 101.325 → 16.212.</summary>
    public static uint PaToMillibars16(long pa) =>
        (uint)Math.Round(pa * 16 / 100.0, MidpointRounding.AwayFromZero);

    /// <summary>KOHLSMAN SETTING MB para Pa inteiro: 1013,25 → 101.325.</summary>
    public static long MillibarsToPa(double mb) =>
        (long)Math.Round(mb * 100, MidpointRounding.AwayFromZero);

    /// <summary>Frequência em Hz pedida como FLOAT64 ao simulador, sem o ruído de ponto flutuante.</summary>
    public static long RoundHz(double hz) =>
        (long)Math.Round(hz, MidpointRounding.AwayFromZero);

    private static uint DecimalToBcd(long value, int digits)
    {
        uint bcd = 0;
        var rest = value;
        for (var i = 0; i < digits && rest > 0; i++)
        {
            bcd |= (uint)(rest % 10) << (4 * i);
            rest /= 10;
        }
        return bcd;
    }
}
