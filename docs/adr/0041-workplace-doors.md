# Workplace doors

> Status: accepted. Builds on ADR 0026 (Occupations, Roles), 0027 (Openings), 0030 (Sessions), 0035 (private Places),
> 0036 (Keys) and 0039 (the CDU is a secret). Vocabulary in `CONTEXT.md` (§ Places).

An opened office was a public Place like the Café. Anyone could walk in, and trams pointed at whatever was busiest, so
one evening Pieter, Inês, Denise and a stranger all ended up in The Works, the CDU's office. Justin met a stranger at his
desk on a Saturday night and made small talk.

## Decision

1. **A workplace has a door: open, by appointment, or staff only.** It lives on the Place (`Access`), so Places stays
   the one writer of who may enter. A Place with no staff, or an open door, lets anyone in.
2. **Staff come from Occupations.** An Occupation tells Places where it works whenever that changes, including on the
   first clock step after a deploy. Staff host the door, as a Home's owner does: they hear knocks and let people in.
3. **The kit sets the first door.** Offices and studios are staff only, clinics by appointment, and everything else
   (cafés, restaurants, shops, parks) is open. A practice opens as a clinic. Once staffed, the door is the author's.
4. **The author keeps an access list.** People on it come and go like staff. Changing the door or the list sends anyone
   no longer allowed home. Set in Work & Wants under Doors (`PUT /places/{id}/access`).
5. **Appointments and get-togethers let their people in.** A Session lets its Client in, and a Gathering its
   attendees, as guests until the last staffer leaves. Only when the Practitioner or host may enter themselves, so
   nobody books a party in someone else's office.
6. **No Keys to a workplace.** Keys stay what ADR 0036 made them, between people close to a Home's host. A workplace
   door is the author's, so no Key Desire forms at one and staff invite nobody over.
7. **Inside, the door is a fact.** Staff read who doesn't work here and how they got in (on the list, let in, or no idea
   how), and a visitor reads that they're visiting. That is what lets someone notice a stranger at their desk.

## Considered

- **Keys for staff.** One mechanism, but Keys carry Relations (asked for, given back after a break-up). A job isn't
  that, and the author couldn't tell staff keys from personal ones.
- **A door per Company.** Practices and staff of a Place without a Company (a barista) need doors too.
- **Closed by default for every Company.** A restaurant is a workplace that wants strangers in.

## Consequences

- Prod's offices close on the first clock step after deploy, and anyone inside who isn't staff walks home.
- A Player has to knock at an office, like a Persona.
- An office whose staff all leave their jobs opens again.
