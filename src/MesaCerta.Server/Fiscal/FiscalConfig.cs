namespace MesaCerta.Server.Fiscal;

/// <summary>Dados provisórios do emitente usados no XML de pré-autorização.</summary>
public sealed record FiscalConfig(
    string Cnpj,
    string Ie,
    string LegalName,
    string TradeName,
    string Street,
    string Number,
    string District,
    string CityCode,
    string City,
    string State,
    string ZipCode)
{
    public static readonly FiscalConfig Default = FromEnv(new Dictionary<string, string?>());

    public static FiscalConfig FromEnv(IReadOnlyDictionary<string, string?> env)
    {
        string Text(string key, string fallback) =>
            env.GetValueOrDefault(key) is { Length: > 0 } value ? value : fallback;
        return new FiscalConfig(
            Cnpj: OnlyDigits(env.GetValueOrDefault("FISCAL_CNPJ"), 14, "00000000000000"),
            Ie: OnlyDigits(env.GetValueOrDefault("FISCAL_IE"), 10, "0000000000"),
            LegalName: Text("FISCAL_RAZAO_SOCIAL", "EMITENTE NAO CONFIGURADO"),
            TradeName: Text("FISCAL_NOME_FANTASIA", "MESACERTA"),
            Street: Text("FISCAL_LOGRADOURO", "ENDERECO NAO CONFIGURADO"),
            Number: Text("FISCAL_NUMERO", "0"),
            District: Text("FISCAL_BAIRRO", "NAO CONFIGURADO"),
            CityCode: OnlyDigits(env.GetValueOrDefault("FISCAL_CODIGO_MUNICIPIO"), 7, "4106902"),
            City: Text("FISCAL_MUNICIPIO", "CURITIBA"),
            State: Text("FISCAL_UF", "PR"),
            ZipCode: OnlyDigits(env.GetValueOrDefault("FISCAL_CEP"), 8, "80000000"));
    }

    private static string OnlyDigits(string? value, int length, string fallback)
    {
        var digits = new string((value ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length == length ? digits : fallback;
    }
}
