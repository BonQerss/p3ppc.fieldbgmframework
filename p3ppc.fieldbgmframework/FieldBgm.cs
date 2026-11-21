namespace p3ppc.fieldbgmframework
{
    // One entry of a mod's bgm/fieldbgm.json, -1 means any
    public class FieldBgm
    {
        public int Adx { get; set; } = -1;
        public int MajorId { get; set; } = -1;
        public int MinorId { get; set; } = -1;
        public int TartarusFloor { get; set; } = -1;
        public int Time { get; set; } = -1; // 0 morning, 1 afternoon, 2 evening, 5 night
        public bool FemcOnly { get; set; }
        public bool MaleOnly { get; set; }
        public int Flag { get; set; } = -1;
        public int StartMonth { get; set; } = 1;
        public int StartDay { get; set; } = 1;
        public int EndMonth { get; set; } = 12;
        public int EndDay { get; set; } = 31;

        public bool Matches(int major, int minor, int floor, int time, bool femc, DateTime date)
        {
            return Any(MajorId, major) && Any(MinorId, minor) && Any(TartarusFloor, floor) && Any(Time, time) && !(FemcOnly && !femc) && !(MaleOnly && femc) && InDateRange(date);
        }

        public bool SamePlaceAndTime(FieldBgm other)
        {
            return MajorId == other.MajorId && MinorId == other.MinorId && TartarusFloor == other.TartarusFloor && Time == other.Time && FemcOnly == other.FemcOnly && MaleOnly == other.MaleOnly;
        }

        private static bool Any(int wanted, int value) => wanted == -1 || wanted == value;

        // A range whose start is after its end wraps around the new year
        private bool InDateRange(DateTime date)
        {
            if (StartMonth == -1 || EndMonth == -1)
            {
                return true;
            }

            int today = date.Month * 100 + date.Day;
            int start = StartMonth * 100 + Math.Max(StartDay, 1);
            int end = EndMonth * 100 + (EndDay == -1 ? 31 : EndDay);
            return start <= end ? today >= start && today <= end : today >= start || today <= end;
        }

        public override string ToString()
        {
            return $"BGM {Adx} in field {MajorId}_{MinorId}, floor {TartarusFloor}, time {Time}, femc {FemcOnly}, male {MaleOnly}, flag {Flag}, {StartMonth}/{StartDay} to {EndMonth}/{EndDay}";
        }
    }
}
