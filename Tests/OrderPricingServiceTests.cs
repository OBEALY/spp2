using MiniTestFramework;
using TestedProject;

namespace Tests;

[TestClass]
public sealed class OrderPricingServiceTests
{
    private OrderPricingService _service = null!;

    [BeforeAll]
    private void Init()
    {
        _service = new OrderPricingService();
    }

    [Test]
    [TestInfo("Regular order: no discount, paid delivery", priority: 1)]
    public void CalculateTotal_RegularOrder_ShouldIncludeDelivery()
    {
        var items = new[]
        {
            new OrderItem("BOOK", 20m, 2),
            new OrderItem("PEN", 5m, 3)
        };

        var result = _service.CalculateTotal(items);

        AssertEx.Equal(55m, result.Subtotal);
        AssertEx.Equal(0m, result.DiscountAmount);
        AssertEx.Equal(7.99m, result.DeliveryFee);
        AssertEx.Equal(0m, result.PrioritySurcharge);
        AssertEx.Equal(62.99m, result.Total);
    }

    [Test]
    [TestInfo("Order over threshold gets free delivery", priority: 1)]
    public void CalculateTotal_OverThreshold_ShouldBeFreeDelivery()
    {
        var items = new[]
        {
            new OrderItem("HEADPHONES", 60m, 2)
        };

        var result = _service.CalculateTotal(items);

        AssertEx.Equal(120m, result.Subtotal);
        AssertEx.Equal(0m, result.DeliveryFee);
        AssertEx.Equal(120m, result.Total);
    }

    [Test]
    [TestInfo("Discount is applied before delivery threshold check", priority: 2)]
    public void CalculateTotal_DiscountCanDisableFreeDelivery()
    {
        var items = new[]
        {
            new OrderItem("KEYBOARD", 110m, 1)
        };

        var result = _service.CalculateTotal(items, discountPercent: 10m);

        AssertEx.Equal(110m, result.Subtotal);
        AssertEx.Equal(11m, result.DiscountAmount);
        AssertEx.Equal(7.99m, result.DeliveryFee);
        AssertEx.Equal(106.99m, result.Total);
    }

    [Test]
    [TestInfo("Priority shipping adds surcharge", priority: 2)]
    public void CalculateTotal_PriorityDelivery_ShouldAddSurcharge()
    {
        var items = new[]
        {
            new OrderItem("MUG", 15m, 2)
        };

        var result = _service.CalculateTotal(items, priorityDelivery: true);

        AssertEx.Equal(30m, result.Subtotal);
        AssertEx.Equal(7.99m, result.DeliveryFee);
        AssertEx.Equal(5m, result.PrioritySurcharge);
        AssertEx.Equal(42.99m, result.Total);
    }

    [Test]
    [TestInfo("Discount boundaries are validated", priority: 3)]
    public void CalculateTotal_InvalidDiscount_ShouldThrow()
    {
        var items = new[]
        {
            new OrderItem("LAMP", 25m, 1)
        };

        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateTotal(items, discountPercent: 80m));
    }

    [Test]
    [TestInfo("Item quantity and price are validated", priority: 3)]
    public void CalculateTotal_InvalidItem_ShouldThrow()
    {
        var items = new[]
        {
            new OrderItem("TABLE", 0m, 1)
        };

        AssertEx.Throws<ArgumentOutOfRangeException>(() =>
            _service.CalculateTotal(items));
    }

    [Test]
    [TestInfo("Order must contain items", priority: 3)]
    public void CalculateTotal_EmptyOrder_ShouldThrow()
    {
        var items = Array.Empty<OrderItem>();

        AssertEx.Throws<ArgumentException>(() =>
            _service.CalculateTotal(items));
    }

    [TestCase(0)]
    [TestCase(50)]
    [TestInfo("Discount boundary values should be accepted", priority: 2)]
    public void CalculateTotal_DiscountBoundaryValues_ShouldWork(int discountPercent)
    {
        var items = new[]
        {
            new OrderItem("BAG", 40m, 2)
        };

        var result = _service.CalculateTotal(items, discountPercent: discountPercent);
        AssertEx.Greater(result.Total, 0m);
    }

    [Test]
    [TestInfo("Money values are rounded to 2 decimals", priority: 2)]
    public void CalculateTotal_ShouldRoundMoneyValues()
    {
        var items = new[]
        {
            new OrderItem("CABLE", 19.995m, 1)
        };

        var result = _service.CalculateTotal(items, discountPercent: 5m);

        AssertEx.Equal(20.00m, result.Subtotal);
        AssertEx.Equal(1.00m, result.DiscountAmount);
        AssertEx.Equal(26.99m, result.Total);
    }
}
