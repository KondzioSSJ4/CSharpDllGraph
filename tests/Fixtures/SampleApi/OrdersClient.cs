using System.Net.Http;
using System.Threading.Tasks;

namespace SampleApi;

public sealed class OrdersClient
{
    private readonly HttpClient _httpClient;

    public OrdersClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task GetOrdersAsync()
    {
        return _httpClient.GetAsync("/api/orders");
    }

    public Task SubmitOrderAsync(int orderId)
    {
        return _httpClient.PostAsync($"/api/orders/{orderId}/submit", null);
    }

    public Task DeleteOrderAsync(int orderId)
    {
        return _httpClient.DeleteAsync("/api/orders/" + orderId);
    }
}
