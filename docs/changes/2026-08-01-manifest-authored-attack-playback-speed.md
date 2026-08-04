# Manifest-authored attack playback speed

Weapons author `attack_playback_speed`; the assigned swing Rule Block declares
it as the presentation source. Authority evaluates and returns the rate with
`AttackLight`, while the Manifest remains at `1.0x`. Unity applies only the
approved multiplier. Missing Triple or Rule Block fails closed without fallback.
