# Account entry state surface

The account-to-world entry coordinator now exposes character/world lists,
selected IDs, and a `StateChanged` event for a future hierarchy-authored login
and character-selection UI. The development identity remains automatic; no
credentials or account data are moved into world Facts. Saved appearance is
still projected only through `ApplyAccountProfile` when world entry succeeds.

