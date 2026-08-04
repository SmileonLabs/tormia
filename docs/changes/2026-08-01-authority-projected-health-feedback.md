# Authority-projected actor health feedback

Player and monster floating health numbers now present only confirmed changes
to the Authority projection's canonical `current_health` fact. The initial value
is silent; decreases show damage and increases show healing. Binding uses the
durable entity GUID and visual bounds, with no prefab- or name-based gameplay
fallback. Missing, invalid, or duplicate health facts fail closed.
