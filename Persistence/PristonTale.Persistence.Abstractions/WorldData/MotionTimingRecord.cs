namespace PristonTale.Persistence.Abstractions.WorldData;

public sealed record MotionModelRecord(
    string ModelKey,
    string MotionTableKey);

public sealed record MotionSlotRecord(
    string MotionTableKey,
    int Slot,
    int State,
    int StartFrame,
    int EndFrame,
    long EventFrame0,
    long EventFrame1,
    long EventFrame2,
    long EventFrame3,
    bool Repeat,
    int KeyCode = 0);
