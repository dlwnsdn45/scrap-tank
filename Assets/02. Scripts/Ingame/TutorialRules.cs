internal enum TutorialStep
{
    Welcome, Health, Timer, Minimap, Move, Aim, Boost, Recharge, BoostAgain, Pickup, Fire, Swap,
    AssemblyOpen, AssemblyMove, AssemblyDismantle, AssemblyInstall, AssemblyClose,
    FieldRepair, StationRepair, PauseOpen, SettingsOpen, SettingsClose, PauseClose
}

internal enum TutorialAction
{
    Move, Aim, Boost, Collect, Fire, Swap, AssemblyOpen, AssemblyMove,
    AssemblyDismantle, AssemblyInstall, AssemblyClose, FieldRepair, StationRepair,
    PauseOpen, SettingsOpen, SettingsClose, PauseClose
}

// Pure rules shared by gameplay and the lesson UI. No input can bypass a lesson gate.
internal static class TutorialRules
{
    internal static bool Allows(TutorialStep step, TutorialAction action) => action switch
    {
        TutorialAction.Move => step is TutorialStep.Move or TutorialStep.Boost or TutorialStep.BoostAgain or
            TutorialStep.Pickup or TutorialStep.Fire or TutorialStep.StationRepair,
        TutorialAction.Aim => step >= TutorialStep.Aim && step <= TutorialStep.StationRepair,
        TutorialAction.Boost => step is TutorialStep.Boost or TutorialStep.BoostAgain,
        TutorialAction.Collect => step is TutorialStep.Move or TutorialStep.Pickup,
        TutorialAction.Fire => step == TutorialStep.Fire,
        TutorialAction.Swap => step == TutorialStep.Swap,
        TutorialAction.AssemblyOpen => step == TutorialStep.AssemblyOpen,
        TutorialAction.AssemblyMove => step == TutorialStep.AssemblyMove,
        TutorialAction.AssemblyDismantle => step == TutorialStep.AssemblyDismantle,
        TutorialAction.AssemblyInstall => step == TutorialStep.AssemblyInstall,
        TutorialAction.AssemblyClose => step == TutorialStep.AssemblyClose,
        TutorialAction.FieldRepair => step == TutorialStep.FieldRepair,
        TutorialAction.StationRepair => step == TutorialStep.StationRepair,
        TutorialAction.PauseOpen => step == TutorialStep.PauseOpen,
        TutorialAction.SettingsOpen => step == TutorialStep.SettingsOpen,
        TutorialAction.SettingsClose => step == TutorialStep.SettingsClose,
        TutorialAction.PauseClose => step == TutorialStep.PauseClose,
        _ => false
    };

    internal static bool NeedsConfirmation(TutorialStep step) => step is TutorialStep.Welcome or
        TutorialStep.Health or TutorialStep.Timer or TutorialStep.Minimap;

    internal static bool IsUiPractice(TutorialStep step) => step is >= TutorialStep.AssemblyOpen and <= TutorialStep.AssemblyClose
        or >= TutorialStep.PauseOpen and <= TutorialStep.PauseClose;
}
