using PaqAgent.Options;
using PaqAgent;

namespace PaqAgent.Tests;

public class SqlConnectionStringFactoryTests
{
    [Fact]
    public void ResolveDataSource_con_puerto_sin_instancia()
    {
        var sql = new SqlOptions { Server = "192.168.1.10", Port = 1433 };
        Assert.Equal("192.168.1.10,1433", SqlConnectionStringFactory.ResolveDataSource(sql));
    }

    [Fact]
    public void ResolveDataSource_named_instance_ignora_puerto()
    {
        var sql = new SqlOptions { Server = @"SERVIDOR\AXSQL", Port = 1433 };
        Assert.Equal(@"SERVIDOR\AXSQL", SqlConnectionStringFactory.ResolveDataSource(sql));
    }
}
