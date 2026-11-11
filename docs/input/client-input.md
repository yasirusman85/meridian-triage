# Client input (verbatim from the Solvefy test task, section 2)

This is the only input we have from the client. It is deliberately incomplete and partly contradictory. We may not ask for clarification.

## The client
Meridian Property Services: a residential property management company in Manchester, UK. They manage about 1,100 rented flats and houses across roughly 60 buildings for private landlords. Tenants report maintenance problems; Meridian sends a contractor.

## 2.1 Notes from a call with Dawn, Operations Director
"Right, so Rachel is our maintenance coordinator and Rachel is drowning. Everything comes into one inbox — maintenance@ — and she reads every single one, works out what it is, how bad it is, which contractor to send, and raises the job in our system. Three hundred, four hundred a month, more in winter, obviously, when the boilers go.

The problem is she's the bottleneck. She's off sick, nothing moves. She's on holiday, we put someone else on it who doesn't know the buildings and it's a disaster. And honestly a lot of it is the same five things over and over.

What I want is for this to be automated. Rachel shouldn't be sitting there reading emails — that's not a job for a person in 2026.

The categories are basically plumbing, electrical, heating, appliances, locks and doors, pest, and then a general bucket for everything else. We've got contractors for each, roughly.

Emergencies have to be dealt with straight away. No heating in winter is an emergency, water coming through a ceiling is an emergency, anything gas is an emergency. We promise 24 hours on emergencies and five working days on everything else, that's in the tenancy agreement.

Obviously nothing goes out to a contractor without someone checking it first, we can't have the system booking a plumber on its own.

Some of them send WhatsApps as well now, which we don't really have a process for.

If this works, Rachel does the exceptions and the difficult ones and we stop losing two days every time she's away."

## 2.2 Six messages Dawn forwarded, "so you can see what we deal with"
1. From: j.okafor@..., Tue 10:42 "Hi there the tap in the bathroom has been dripping for about two weeks now, it's not urgent but it's quite annoying at night. Also while I'm emailing, the light in the hallway cupboard doesn't work, I think it's just the bulb but I'm not allowed to change it myself am I. Flat 4, 22 Pemberton Road. Thanks, Joy"
2. From: m.walsh@..., Fri 19:15 "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT."
3. From: m.walsh@..., Fri 19:51 "Still nothing. Michael Walsh, 14b Alder Court."
4. From: s.walsh@..., Fri 20:30 "My husband has emailed twice about the heating at 14b Alder Court, no one has responded. We have a 7 month old baby. What is going on."
5. From: a.nowak@..., Wed 08:03 "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję." (two photos attached)
6. From: r.patel@..., Mon 14:20 "The boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before. 8 Carlton Street."

## 2.3 WhatsApp from Dawn, the following week
"Forgot to say — the MD wants to see numbers out of this. How many jobs, how fast, that sort of thing. Don't go mad on it."

## Build constraints from the task (section 3)
- Backend: .NET 8 or 9, C#, ASP.NET Core. Relational DB (SQLite fine).
- An AI component inside the product that processes incoming messages; model, provider, prompt strategy and output contract are our choice. Model sits behind our own interface; a stub with recorded responses is acceptable.
- Front end sufficient to demonstrate the workflow (design not assessed).
- Must run in five minutes from one command or a short list of steps.
- We don't have to build everything; choosing what to build first and what to leave out is assessed.
- Planned model provider: OpenRouter (key added at the end); stub provider is the default.
