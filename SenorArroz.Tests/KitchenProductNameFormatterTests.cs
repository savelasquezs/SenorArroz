using SenorArroz.Application.Common.Printing;

namespace SenorArroz.Tests;

public class KitchenProductNameFormatterTests
{
    [Fact]
    public void Omitted_words_and_trocitos_chich() =>
        Assert.Equal("Trocitos chich", KitchenProductNameFormatter.Format("Trocitos de chicharrón"));

    [Fact]
    public void Ropa_vieja_super_reorders() =>
        Assert.Equal("super ropa chich", KitchenProductNameFormatter.Format("Arroz ropa vieja con chicharrón Súper"));

    [Fact]
    public void Super_and_Familiar_drops_familiar() =>
        Assert.Equal("super paisa", KitchenProductNameFormatter.Format("arroz paisa Súper Familiar"));

    [Fact]
    public void Papas_ala_francesa_500g() =>
        Assert.Equal("Papas 500g", KitchenProductNameFormatter.Format("Papas a la francesa 500 gr"));

    [Fact]
    public void Yuca_x10_unidades() =>
        Assert.Equal("Yuca x 10", KitchenProductNameFormatter.Format("Yuca x10 unidades"));

    [Fact]
    public void Combochicharron() =>
        Assert.Equal("Combochich", KitchenProductNameFormatter.Format("Combochicharrón"));

    [Fact]
    public void All_dropped_falls_back_to_original() =>
        Assert.Equal("arroz con", KitchenProductNameFormatter.Format("arroz con"));
}
