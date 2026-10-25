using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Application.Common;
using Finance.Domain.Accounts;
using Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.IntegrationTests;

public sealed partial class FinanceApiTests
{
    [Fact]
    public async Task Transfer_IsAtomicAndIdempotent_DoesNotAffectReports_AndCanBeCancelled()
    {
        using var client = await User();
        var source = (await Accounts(client))[0];
        var target = await NewAccount(client, "Наличные", "100");
        var sourceId = source.GetProperty("id").GetGuid();
        var targetId = target.GetProperty("id").GetGuid();
        var payload = new { requestId = Guid.NewGuid(), sourceAccountId = sourceId, targetAccountId = targetId, amount = "25.01", description = "  В кошелёк  " };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/transfers", payload), client.PostAsJsonAsync("/api/transfers", payload));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var first = await Json(responses[0]);
        Assert.Equal(first.GetProperty("id").GetGuid(), (await Json(responses[1])).GetProperty("id").GetGuid());
        var balances = await Accounts(client);
        Assert.Equal("-25.01", balances.Single(a => a.GetProperty("id").GetGuid() == sourceId).GetProperty("balance").GetString());
        Assert.Equal("125.01", balances.Single(a => a.GetProperty("id").GetGuid() == targetId).GetProperty("balance").GetString());
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Equal("0.00", dashboard.GetProperty("income").GetString());
        Assert.Equal("0.00", dashboard.GetProperty("expense").GetString());
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transactions")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/transfers", new { payload.requestId, payload.sourceAccountId, payload.targetAccountId, amount = "26", payload.description })).StatusCode);
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/transfers/{id}/cancel", new { version = Guid.NewGuid() })).StatusCode);
        var cancellation = new { version = first.GetProperty("version").GetGuid() };
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/transfers/{id}/cancel", cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/transfers/{id}/cancel", cancellation)).StatusCode);
        var replay = await client.PostAsJsonAsync("/api/transfers", payload);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await Json(replay)).GetProperty("cancelledAtUtc").ValueKind);
        Assert.Equal("0.00", (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() == sourceId).GetProperty("balance").GetString());
        Assert.Equal("100.00", (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() == targetId).GetProperty("balance").GetString());
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/transfers")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Transfers_EnforceOwnershipValidationArchiveAndPagination()
    {
        using var owner = await User();
        using var other = await User();
        var source = (await Accounts(owner))[0];
        var sourceId = source.GetProperty("id").GetGuid();
        var target = await NewAccount(owner);
        var targetId = target.GetProperty("id").GetGuid();
        var foreignId = (await Accounts(other))[0].GetProperty("id").GetGuid();
        async Task<HttpResponseMessage> Post(Guid from, Guid to, string amount = "1") => await owner.PostAsJsonAsync("/api/transfers",
            new { requestId = Guid.NewGuid(), sourceAccountId = from, targetAccountId = to, amount });
        foreach (var amount in new[] { "0", "-1", "1.001", "10000000000000000" }) Assert.Equal(HttpStatusCode.BadRequest, (await Post(sourceId, targetId, amount)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(sourceId, sourceId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(sourceId, foreignId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(foreignId, targetId)).StatusCode);
        var first = await Json(await Post(sourceId, targetId));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/transfers/{first.GetProperty("id").GetGuid()}/cancel", new { version = first.GetProperty("version").GetGuid() })).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/transfers")).GetProperty("totalCount").GetInt32());
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Created, (await Post(sourceId, targetId)).StatusCode);
        var page1 = await owner.GetFromJsonAsync<JsonElement>("/api/transfers?page=1&pageSize=2");
        var page2 = await owner.GetFromJsonAsync<JsonElement>("/api/transfers?page=2&pageSize=2");
        Assert.Equal(3, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page2.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/transfers?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/accounts/{targetId}", new { name = "Карта", isArchived = true, version = target.GetProperty("version").GetGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(sourceId, targetId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(targetId, sourceId)).StatusCode);
    }

    [Fact]
    public async Task Transfer_RejectsAccountArchivedAfterValidation()
    {
        using var client = await User();
        var target = await NewAccount(client);
        var targetId = target.GetProperty("id").GetGuid();
        var sourceId = (await Accounts(client)).Single(a => a.GetProperty("id").GetGuid() != targetId).GetProperty("id").GetGuid();
        using var scope = fixture.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var source = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == sourceId);
        var destination = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == targetId);
        var pending = AccountTransfer.Create(source.UserId, Guid.NewGuid(), source, destination, 10m, null, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/accounts/{targetId}", new { name = destination.Name, isArchived = true, version = destination.Version })).StatusCode);
        await Assert.ThrowsAsync<ConflictException>(() => new TransferStore(db).AddAsync(pending, CancellationToken.None));
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/transfers")).GetProperty("totalCount").GetInt32());
    }
}
