using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class AdminKeepersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;
    private readonly string _dataRoot;

    public AdminKeepersEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _dataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_configRoot);
        Directory.CreateDirectory(_dataRoot);

        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["DataRoot"] = _dataRoot,
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose()
    {
        Directory.Delete(_configRoot, recursive: true);
        Directory.Delete(_dataRoot, recursive: true);
    }

    private static byte[] BuildWorkbook(string teamName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("2026 Keepers");
        sheet.Cell(6, "A").Value = teamName;
        sheet.Cell(7, "C").Value = "Player";
        sheet.Cell(7, "D").Value = "Contract 1 or 2?";
        sheet.Cell(7, "G").Value = "Existing Contracts";
        sheet.Cell(8, "C").Value = "Some Player";
        sheet.Cell(8, "D").Value = 1;
        sheet.Cell(8, "E").Value = 5;
        sheet.Cell(8, "F").Value = 1;
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task ImportKeepers_WithOwnerPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(BuildWorkbook("B Squared")), "file", "keepers.xlsx" }
        };

        var response = await client.PostAsync("/api/admin/keepers/import?pin=1111", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ImportThenConfirm_ThenGetKeepers_ReturnsImportedData()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(BuildWorkbook("B Squared")), "file", "keepers.xlsx" }
        };

        var importResponse = await client.PostAsync("/api/admin/keepers/import?pin=9999", content);
        importResponse.EnsureSuccessStatusCode();
        var preview = await importResponse.Content.ReadFromJsonAsync<ImportPreview>(ResponseJsonOptions);
        Assert.Equal("b-squared", preview!.Blocks[0].SuggestedTeamId);
        Assert.Equal("2026 Keepers", preview.SheetName);

        var confirmRequest = new ConfirmImportRequest(new List<BlockAssignment> { new(0, "b-squared") });
        var confirmResponse = await client.PostAsJsonAsync("/api/admin/keepers/import/confirm?pin=9999", confirmRequest);
        confirmResponse.EnsureSuccessStatusCode();

        var keepersResponse = await client.GetAsync("/api/keepers?pin=1111&teamId=b-squared");
        keepersResponse.EnsureSuccessStatusCode();
        var data = await keepersResponse.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("Some Player", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task ImportKeepers_UnreadableFile_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 }), "file", "not-a-workbook.xlsx" }
        };

        var response = await client.PostAsync("/api/admin/keepers/import?pin=9999", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportKeepers_ParserFailsWithNonInvalidWorkbookException_ReturnsBadRequestNot500()
    {
        // A header anchor on row 1 leaves no team-name row above it, which makes the parser
        // throw an out-of-range exception rather than InvalidWorkbookException.
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("2026 Keepers");
            sheet.Cell(1, "C").Value = "Player";
            sheet.Cell(1, "D").Value = "Contract 1 or 2?";
            sheet.Cell(1, "G").Value = "Existing Contracts";
            sheet.Cell(2, "C").Value = "Some Player";
            using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            bytes = ms.ToArray();
        }

        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "file", "row-one-anchor.xlsx" }
        };

        var response = await client.PostAsync("/api/admin/keepers/import?pin=9999", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportKeepers_BeforeAnyImport_ReturnsConflict()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/keepers/export?pin=9999");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_BeforeAnyImport_ReturnsNullTimestamp()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/keepers/status?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("lastUpdatedUtc").ValueKind);
    }
}
