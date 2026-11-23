using MesaCerta.Server.Config;

namespace MesaCerta.Tests;

public class ConfigTests
{
    private const string DatabaseUrl = "mysql://user:secret-password@localhost:3306/test";

    [Fact]
    public void Configuracao_rejeita_porta_e_url_invalidas_sem_expor_credenciais()
    {
        Assert.Equal(5050, ServerConfig.Load(new Dictionary<string, string?> { ["DATABASE_URL"] = DatabaseUrl }).Port);
        foreach (var env in new[]
        {
            new Dictionary<string, string?> { ["DATABASE_URL"] = DatabaseUrl, ["SERVER_PORT"] = "70000" },
            new Dictionary<string, string?> { ["DATABASE_URL"] = "postgres://user:secret-password@localhost/test" },
            new Dictionary<string, string?>(),
        })
        {
            var error = Assert.Throws<ConfigurationException>(() => ServerConfig.Load(env));
            Assert.DoesNotContain("secret-password", error.Message);
            Assert.StartsWith("Configuração inválida", error.Message);
        }
    }

    [Fact]
    public void Url_mysql_vira_connection_string_com_senha_decodificada()
    {
        var config = ServerConfig.Load(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "mysql://app:p%40ss%3Aword@db.local/restaurante",
            ["SERVER_HOST"] = "0.0.0.0",
            ["SERVER_PORT"] = "6000",
        });
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(config.ConnectionString);
        Assert.Equal("p@ss:word", builder.Password);
        Assert.Equal("app", builder.UserID);
        Assert.Equal(3306u, builder.Port);
        Assert.Equal("restaurante", config.DatabaseName);
        Assert.Equal(("0.0.0.0", 6000), (config.Host, config.Port));
    }

    [Fact]
    public void DotEnv_le_aspas_comentarios_e_linhas_vazias()
    {
        var values = DotEnv.Parse([
            "# comentário",
            "",
            "DATABASE_URL=\"mysql://a:b@c/d\"",
            "SERVER_PORT=5050 # porta",
            "export FISCAL_UF='PR'",
        ]).ToDictionary();
        Assert.Equal("mysql://a:b@c/d", values["DATABASE_URL"]);
        Assert.Equal("5050", values["SERVER_PORT"]);
        Assert.Equal("PR", values["FISCAL_UF"]);
    }
}
