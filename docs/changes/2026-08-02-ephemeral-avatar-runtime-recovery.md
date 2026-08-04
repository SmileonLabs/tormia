# Ephemeral avatar runtime recovery

Treat a missing runtime avatar after an Authority/Redis restart as one
ephemeral-session recovery condition, not separate equipment and combat
failures. Heartbeat and runtime-position reads reactivate the accepted avatar
from its durable checkpoint only when missing. Gameplay remains fail-closed
until recovery succeeds.
