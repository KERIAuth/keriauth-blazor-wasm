using System.Globalization;
using Extension.Helper;

namespace Extension.Tests.Helper {
    public class DateDisplayFormatterTests {
        private static readonly DateTimeOffset Now = new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void ThirtySecondsOld_ReturnsJustNow() {
            var ts = Now.AddSeconds(-30);
            Assert.Equal("Just now", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void OneMinuteOld_ReturnsOneMinuteAgo() {
            var ts = Now.AddMinutes(-1);
            Assert.Equal("1m ago", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void SeventeenMinutesOld_ReturnsSeventeenMinutesAgo() {
            var ts = Now.AddMinutes(-17);
            Assert.Equal("17m ago", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void FiftyNineMinutesOld_TruncatesDownward() {
            var ts = Now.AddMinutes(-59).AddSeconds(-59);
            Assert.Equal("59m ago", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void OneHourOld_ReturnsOneHourAgo() {
            var ts = Now.AddHours(-1);
            Assert.Equal("1h ago", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void TwentyThreeHoursOld_ReturnsTwentyThreeHoursAgo() {
            var ts = Now.AddHours(-23);
            Assert.Equal("23h ago", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void TwentyFourHoursOld_ReturnsYesterdayWithTime() {
            var ts = Now.AddHours(-24);
            var localTs = ts.ToLocalTime();
            Assert.Equal($"Yesterday {localTs:HH:mm}", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void FortySevenHoursOld_ReturnsYesterdayWithTime() {
            var ts = Now.AddHours(-47);
            var localTs = ts.ToLocalTime();
            Assert.Equal($"Yesterday {localTs:HH:mm}", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void FortyEightHoursOld_ReturnsIsoDate() {
            var ts = Now.AddHours(-48);
            var localTs = ts.ToLocalTime();
            Assert.Equal(localTs.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void FiveDaysOld_ReturnsIsoDate() {
            var ts = Now.AddDays(-5);
            var localTs = ts.ToLocalTime();
            Assert.Equal(localTs.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void FutureByTwoMinutes_ReturnsJustNow() {
            var ts = Now.AddMinutes(2);
            Assert.Equal("Just now", DateDisplayFormatter.FormatHybrid(ts, Now));
        }

        [Fact]
        public void MixedOffsets_NormalizedViaInstant() {
            var nowUtc = new DateTimeOffset(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);
            // 16:30 in +05:00 is 11:30 UTC; elapsed = 30m regardless of local timezone.
            var tsInOtherZone = new DateTimeOffset(2026, 4, 18, 16, 30, 0, TimeSpan.FromHours(5));
            Assert.Equal("30m ago", DateDisplayFormatter.FormatHybrid(tsInOtherZone, nowUtc));
        }

        [Fact]
        public void DateTimeOverload_UtcKindTreatedAsUtc() {
            var tsUtc = DateTime.SpecifyKind(new DateTime(2026, 4, 18, 11, 45, 0), DateTimeKind.Utc);
            Assert.Equal("15m ago", DateDisplayFormatter.FormatHybrid(tsUtc, Now));
        }

        [Fact]
        public void DateTimeOverload_UnspecifiedKindTreatedAsUtc() {
            var tsUnspecified = new DateTime(2026, 4, 18, 11, 45, 0, DateTimeKind.Unspecified);
            Assert.Equal("15m ago", DateDisplayFormatter.FormatHybrid(tsUnspecified, Now));
        }
    }
}
