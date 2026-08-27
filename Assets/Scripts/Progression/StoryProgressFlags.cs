namespace HauntedHouse.Progression
{
    /// <summary>
    /// StoryFlagSet에 저장되는 게임 전체 진행 flag의 고정 ID 모음이다.
    /// Inspector 문자열과 코드가 같은 값을 사용하도록 하여 오타와 중복 명명을 줄인다.
    /// </summary>
    public static class StoryProgressFlags
    {
        public const string IntroDialogueCompleted = nameof(IntroDialogueCompleted);
        public const string StartHallDoorScareSeen = nameof(StartHallDoorScareSeen);
        public const string FlashlightAcquired = nameof(FlashlightAcquired);
        public const string NursingRoomFlashlightActivated = nameof(NursingRoomFlashlightActivated);
        public const string WardHallKeyAcquired = nameof(WardHallKeyAcquired);
        public const string CenterHallFirstEntered = nameof(CenterHallFirstEntered);
        public const string FuseInstalled = nameof(FuseInstalled);
        public const string GeneratorPowered = nameof(GeneratorPowered);
        public const string GateActivated = nameof(GateActivated);
        public const string Sector2HideEncounterCompleted = nameof(Sector2HideEncounterCompleted);
        public const string CenterHallSecondEntered = nameof(CenterHallSecondEntered);
        public const string ExitDoorHintShown = nameof(ExitDoorHintShown);
        public const string FreezerHintRead = nameof(FreezerHintRead);
        public const string BasementKeyAcquired = nameof(BasementKeyAcquired);
        public const string BasementEntered = nameof(BasementEntered);
        public const string KillerDiary3Read = nameof(KillerDiary3Read);
        public const string MasterKeyAcquired = nameof(MasterKeyAcquired);
        public const string FinalChaseStarted = nameof(FinalChaseStarted);
        public const string EntranceReached = nameof(EntranceReached);
        public const string EndingStarted = nameof(EndingStarted);
    }
}
