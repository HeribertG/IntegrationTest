using FluentAssertions;
using Klacks.Api.Application.DTOs.Schedules;
using Klacks.Api.Application.Handlers.ScheduleChanges;
using Klacks.Api.Application.Interfaces;
using Klacks.Api.Infrastructure.Persistence;
using Klacks.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace Klacks.IntegrationTest.WorkSchedule;

[TestFixture]
[Category("RealDatabase")]
public class ScheduleChangeIntegrationTests
{
    private string _connectionString;
    private DataBaseContext _context;
    private ScheduleChangeTracker _tracker;
    private readonly Guid _testClientId1 = Guid.NewGuid();
    private readonly Guid _testClientId2 = Guid.NewGuid();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Port=5434;Database=klacks;Username=postgres;Password=admin";
    }

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<DataBaseContext>()
            .UseNpgsql(_connectionString)
            .Options;

        var mockHttpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _context = new DataBaseContext(options, mockHttpContextAccessor);
        _tracker = new ScheduleChangeTracker(_context);
    }

    [TearDown]
    public async Task TearDown()
    {
        await CleanupTestData();
        _context?.Dispose();
    }

    [Test]
    public async Task TrackChangeAsync_ShouldPersistToDatabase()
    {
        var changeDate = new DateOnly(2026, 2, 19);

        await _tracker.TrackChangeAsync(_testClientId1, changeDate);

        var entry = await _context.ScheduleChange
            .FirstOrDefaultAsync(sc => sc.ClientId == _testClientId1 && sc.ChangeDate == changeDate);

        entry.Should().NotBeNull();
        entry!.ClientId.Should().Be(_testClientId1);
        entry.ChangeDate.Should().Be(changeDate);
        entry.CreateTime.Should().NotBeNull();
        entry.IsDeleted.Should().BeFalse();
    }

    [Test]
    public async Task TrackChangeAsync_DuplicateEntry_ShouldUpdateNotInsert()
    {
        var changeDate = new DateOnly(2026, 2, 19);

        await _tracker.TrackChangeAsync(_testClientId1, changeDate);

        var firstEntry = await _context.ScheduleChange
            .AsNoTracking()
            .FirstAsync(sc => sc.ClientId == _testClientId1 && sc.ChangeDate == changeDate);
        var originalId = firstEntry.Id;

        await Task.Delay(50);
        await _tracker.TrackChangeAsync(_testClientId1, changeDate);

        var entries = await _context.ScheduleChange
            .Where(sc => sc.ClientId == _testClientId1 && sc.ChangeDate == changeDate)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].Id.Should().Be(originalId);
        entries[0].UpdateTime.Should().NotBeNull();
    }

    [Test]
    public async Task TrackChangeAsync_DifferentClientsSameDate_ShouldCreateSeparateEntries()
    {
        var changeDate = new DateOnly(2026, 2, 19);

        await _tracker.TrackChangeAsync(_testClientId1, changeDate);
        await _tracker.TrackChangeAsync(_testClientId2, changeDate);

        var entries = await _context.ScheduleChange
            .Where(sc => sc.ChangeDate == changeDate &&
                         (sc.ClientId == _testClientId1 || sc.ClientId == _testClientId2))
            .ToListAsync();

        entries.Should().HaveCount(2);
    }

    [Test]
    public async Task TrackChangeAsync_SameClientDifferentDates_ShouldCreateSeparateEntries()
    {
        var date1 = new DateOnly(2026, 2, 18);
        var date2 = new DateOnly(2026, 2, 19);

        await _tracker.TrackChangeAsync(_testClientId1, date1);
        await _tracker.TrackChangeAsync(_testClientId1, date2);

        var entries = await _context.ScheduleChange
            .Where(sc => sc.ClientId == _testClientId1)
            .ToListAsync();

        entries.Should().HaveCount(2);
    }

    [Test]
    public async Task GetChangesAsync_ShouldReturnEntriesInDateRange()
    {
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 1, 15));
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 2, 10));
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 2, 20));
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 3, 5));

        var results = await _tracker.GetChangesAsync(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 28));

        var clientResults = results.Where(r => r.ClientId == _testClientId1).ToList();
        clientResults.Should().HaveCount(2);
        clientResults.Select(r => r.ChangeDate).Should().Contain(new DateOnly(2026, 2, 10));
        clientResults.Select(r => r.ChangeDate).Should().Contain(new DateOnly(2026, 2, 20));
    }

    [Test]
    public async Task GetChangesAsync_InclusiveRangeBoundaries()
    {
        var startDate = new DateOnly(2026, 2, 1);
        var endDate = new DateOnly(2026, 2, 28);

        await _tracker.TrackChangeAsync(_testClientId1, startDate);
        await _tracker.TrackChangeAsync(_testClientId1, endDate);
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 1, 31));
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 3, 1));

        var results = await _tracker.GetChangesAsync(startDate, endDate);

        var clientResults = results.Where(r => r.ClientId == _testClientId1).ToList();
        clientResults.Should().HaveCount(2);
        clientResults.Select(r => r.ChangeDate).Should().Contain(startDate);
        clientResults.Select(r => r.ChangeDate).Should().Contain(endDate);
    }

    [Test]
    public async Task FullFlow_TrackAndQuery_ShouldWork()
    {
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 2, 10));
        await _tracker.TrackChangeAsync(_testClientId2, new DateOnly(2026, 2, 15));
        await _tracker.TrackChangeAsync(_testClientId1, new DateOnly(2026, 2, 20));

        var handler = new GetListQueryHandler(
            _tracker,
            Substitute.For<ILogger<GetListQueryHandler>>());

        var query = new GetListQuery
        {
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 2, 28)
        };

        var results = await handler.Handle(query, CancellationToken.None);

        var relevantResults = results
            .Where(r => r.ClientId == _testClientId1 || r.ClientId == _testClientId2)
            .ToList();

        relevantResults.Should().HaveCount(3);
        relevantResults.Where(r => r.ClientId == _testClientId1).Should().HaveCount(2);
        relevantResults.Where(r => r.ClientId == _testClientId2).Should().HaveCount(1);
    }

    [Test]
    public async Task TrackChangeAsync_MultipleUpdates_ShouldKeepLatestTimestamp()
    {
        var changeDate = new DateOnly(2026, 2, 19);

        await _tracker.TrackChangeAsync(_testClientId1, changeDate);
        await Task.Delay(50);
        await _tracker.TrackChangeAsync(_testClientId1, changeDate);
        await Task.Delay(50);
        await _tracker.TrackChangeAsync(_testClientId1, changeDate);

        var entry = await _context.ScheduleChange
            .FirstAsync(sc => sc.ClientId == _testClientId1 && sc.ChangeDate == changeDate);

        entry.UpdateTime.Should().NotBeNull();
        entry.UpdateTime.Should().BeAfter(entry.CreateTime!.Value);
    }

    private async Task CleanupTestData()
    {
        var sql = $"DELETE FROM schedule_change WHERE client_id IN ('{_testClientId1}', '{_testClientId2}')";
        await _context.Database.ExecuteSqlRawAsync(sql);
    }
}
