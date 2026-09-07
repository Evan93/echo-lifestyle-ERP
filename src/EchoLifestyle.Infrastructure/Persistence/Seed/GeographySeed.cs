namespace EchoLifestyle.Infrastructure.Persistence.Seed;

/// <summary>
/// The administrative geography of Bangladesh: eight divisions and sixty-four
/// districts.
///
/// Seeded rather than typed because every courier prices by district and takes
/// them as codes. Getting this in on day one means an address captured today is
/// already shippable when a courier integration arrives; getting it in later
/// means mapping thousands of free-text district names by hand.
///
/// Names use the current official spellings (Chattogram, Cumilla, Bogura,
/// Jashore), which is what courier APIs match on. The older spellings people
/// still type - Chittagong, Comilla, Bogra, Jessore - are handled as search
/// aliases in the lookup, not as separate rows.
/// </summary>
public static class GeographySeed
{
    public sealed record DivisionSeed(string Name, string NameBn, string[] Districts);

    /// <summary>
    /// Districts are listed alphabetically within each division, which is the
    /// order they appear in the dropdown.
    /// </summary>
    public static readonly IReadOnlyList<DivisionSeed> Divisions =
    [
        new("Dhaka", "ঢাকা",
        [
            "Dhaka", "Faridpur", "Gazipur", "Gopalganj", "Kishoreganj", "Madaripur",
            "Manikganj", "Munshiganj", "Narayanganj", "Narsingdi", "Rajbari",
            "Shariatpur", "Tangail",
        ]),

        new("Chattogram", "চট্টগ্রাম",
        [
            "Bandarban", "Brahmanbaria", "Chandpur", "Chattogram", "Cumilla",
            "Cox's Bazar", "Feni", "Khagrachhari", "Lakshmipur", "Noakhali",
            "Rangamati",
        ]),

        new("Khulna", "খুলনা",
        [
            "Bagerhat", "Chuadanga", "Jashore", "Jhenaidah", "Khulna", "Kushtia",
            "Magura", "Meherpur", "Narail", "Satkhira",
        ]),

        new("Rajshahi", "রাজশাহী",
        [
            "Bogura", "Chapainawabganj", "Joypurhat", "Naogaon", "Natore", "Pabna",
            "Rajshahi", "Sirajganj",
        ]),

        new("Rangpur", "রংপুর",
        [
            "Dinajpur", "Gaibandha", "Kurigram", "Lalmonirhat", "Nilphamari",
            "Panchagarh", "Rangpur", "Thakurgaon",
        ]),

        new("Barishal", "বরিশাল",
        [
            "Barguna", "Barishal", "Bhola", "Jhalokati", "Patuakhali", "Pirojpur",
        ]),

        new("Sylhet", "সিলেট",
        [
            "Habiganj", "Moulvibazar", "Sunamganj", "Sylhet",
        ]),

        new("Mymensingh", "ময়মনসিংহ",
        [
            "Jamalpur", "Mymensingh", "Netrokona", "Sherpur",
        ]),
    ];

    /// <summary>
    /// Districts a same-day or next-day service reaches. Everything else is an
    /// outside-city rate, which is roughly double.
    /// </summary>
    public static readonly IReadOnlySet<string> InsideCity =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dhaka" };

    /// <summary>
    /// The spelling each renamed district had before, keyed by its current
    /// name.
    ///
    /// People will keep typing these for years - the renames are recent and the
    /// old forms are printed on everything - so the value is stored on the
    /// district row and shown in the dropdown, rather than living in a lookup
    /// nobody remembers to consult.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FormerNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chattogram"] = "Chittagong",
            ["Cumilla"] = "Comilla",
            ["Bogura"] = "Bogra",
            ["Jashore"] = "Jessore",
            ["Barishal"] = "Barisal",
            ["Chapainawabganj"] = "Chapai Nawabganj",
            ["Khagrachhari"] = "Khagrachari",
            ["Moulvibazar"] = "Maulvibazar",
            ["Netrokona"] = "Netrakona",
            ["Jhenaidah"] = "Jhenidah",
        };
}
