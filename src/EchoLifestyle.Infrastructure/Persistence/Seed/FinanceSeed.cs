namespace EchoLifestyle.Infrastructure.Persistence.Seed;

/// <summary>
/// The categories a small Bangladeshi online cosmetics business actually
/// spends money in.
///
/// A starting list, not a fixed one - the screen lets categories be added, and
/// no list written in advance survives contact with a real business. What is
/// here is chosen so the first month of expenses has somewhere sensible to go
/// on day one, because the alternative is everything landing in "Other" and the
/// report being worthless for a year.
///
/// <c>CostOfSale</c> marks the costs that rise with every parcel. Keeping them
/// apart from the cost of being open at all is what makes "are we making money
/// on this product?" a different question from "are we making money?".
/// </summary>
public static class FinanceSeed
{
    public record CategorySeed(string Name, string Description, bool CostOfSale);

    public static readonly IReadOnlyList<CategorySeed> ExpenseCategories =
    [
        new("Packaging", "Boxes, bubble wrap, tape, branded stickers.", true),
        new("Delivery charges", "Courier charges not deducted from a payout.", true),
        new(
            "Payment charges",
            "bKash, Nagad and bank charges on money coming in or going out.",
            true),
        new("Advertising", "Facebook and Instagram boosting, influencer fees, giveaways.", false),
        new("Product photography", "Shoots, models, props, editing.", false),
        new("Rent", "Space for stock and for working.", false),
        new("Utilities", "Electricity, water, gas, internet, mobile bills.", false),
        new("Salary and wages", "Staff pay, bonuses and festival allowances.", false),
        new("Transport and travel", "Rickshaw, CNG and fuel for collecting or delivering stock.", false),
        new("Office and supplies", "Stationery, printing, small equipment.", false),
        new("Repairs and maintenance", "Fixing anything the business uses.", false),
        new("Government and licences", "Trade licence, registration and professional fees.", false),
        new("Software and subscriptions", "Hosting, domains, design tools, SMS credit.", false),
        new("Other", "Anything with no home yet. If it fills up, add a category.", false),
    ];
}
