public enum BearState
{
    Idle,
    Patrol,
    Tracking,
    InvestigateSound,
    SearchLastKnown,
    Chase               // mode Knows: the player runs away (added at the end - Inspector values stay)
}