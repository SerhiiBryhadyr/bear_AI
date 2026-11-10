public enum BearState
{
    Idle,
    Patrol,
    Tracking,
    InvestigateSound,
    SearchLastKnown,
    Chase,              // mode Knows: the player runs away (added at the end - Inspector values stay)
    Stalk,              // mode Knows: the bear follows the player hidden (D-35, added at the end)
    RearPursuit,        // mode Knows: pattern 1 - pursuit from behind (canon 03, D-52, added at the end)
    Hunt                // mode Knows: aggressive stalking after a bluff, attack from the side (D-52, added at the end)
}
