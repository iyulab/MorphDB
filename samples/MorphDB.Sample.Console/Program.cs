using MorphDB.Client;

Console.WriteLine("===========================================");
Console.WriteLine("  MorphDB Client SDK Sample Application");
Console.WriteLine("===========================================");
Console.WriteLine();

// Configuration - replace with your actual values
var options = new MorphDBClientOptions
{
    BaseUrl = "http://localhost:8080",
    ApiKey = "your-api-key",       // Replace with actual API key
    ProjectId = Guid.NewGuid()      // Replace with actual project ID
};

using var client = new MorphDBClient(options);

try
{
    // ============================================
    // 1. Schema Operations - Create a table
    // ============================================
    Console.WriteLine("[1] Creating 'products' table...");

    var tableRequest = new CreateTableRequest
    {
        LogicalName = "products",
        DisplayName = "Products",
        Description = "Product catalog",
        Columns =
        [
            new CreateColumnRequest
            {
                LogicalName = "name",
                DisplayName = "Product Name",
                DataType = "text",
                IsNullable = false
            },
            new CreateColumnRequest
            {
                LogicalName = "description",
                DisplayName = "Description",
                DataType = "text",
                IsNullable = true
            },
            new CreateColumnRequest
            {
                LogicalName = "price",
                DisplayName = "Price",
                DataType = "decimal",
                IsNullable = false
            },
            new CreateColumnRequest
            {
                LogicalName = "quantity",
                DisplayName = "Stock Quantity",
                DataType = "integer",
                IsNullable = false,
                DefaultValue = "0"
            },
            new CreateColumnRequest
            {
                LogicalName = "is_active",
                DisplayName = "Is Active",
                DataType = "boolean",
                IsNullable = false,
                DefaultValue = "true"
            }
        ]
    };

    var table = await client.Schema.CreateTableAsync(tableRequest);
    Console.WriteLine($"   ✓ Created table: {table.LogicalName} (ID: {table.TableId})");
    Console.WriteLine();

    // ============================================
    // 2. Data Operations - Insert records
    // ============================================
    Console.WriteLine("[2] Inserting sample products...");

    var products = new[]
    {
        new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["name"] = "Laptop Pro 15",
            ["description"] = "High-performance laptop with 15-inch display",
            ["price"] = 1299.99m,
            ["quantity"] = 50,
            ["is_active"] = true
        },
        new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["name"] = "Wireless Mouse",
            ["description"] = "Ergonomic wireless mouse with long battery life",
            ["price"] = 49.99m,
            ["quantity"] = 200,
            ["is_active"] = true
        },
        new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["name"] = "USB-C Hub",
            ["description"] = "Multi-port USB-C hub with HDMI output",
            ["price"] = 79.99m,
            ["quantity"] = 100,
            ["is_active"] = true
        }
    };

    foreach (var product in products)
    {
        var inserted = await client.Data.InsertAsync("products", product);
        Console.WriteLine($"   ✓ Inserted: {inserted["name"]}");
    }
    Console.WriteLine();

    // ============================================
    // 3. Query Operations - List and filter
    // ============================================
    Console.WriteLine("[3] Querying products...");

    // List all products
    var allProducts = await client.Data.ListAsync("products");
    Console.WriteLine($"   Total products: {allProducts.Count}");

    // Filter by price
    var expensiveProducts = await client.Data.QueryAsync("products", new QueryRequest
    {
        Filter = new FilterCondition
        {
            Field = "price",
            Operator = "gte",
            Value = 100
        },
        OrderBy = new[] { new OrderByClause { Field = "price", Direction = "desc" } },
        Limit = 10
    });
    Console.WriteLine($"   Products >= $100: {expensiveProducts.Count}");

    foreach (var p in expensiveProducts)
    {
        Console.WriteLine($"     - {p["name"]}: ${p["price"]}");
    }
    Console.WriteLine();

    // ============================================
    // 4. Update Operation
    // ============================================
    Console.WriteLine("[4] Updating product quantity...");

    var productToUpdate = allProducts.First();
    var productId = Guid.Parse(productToUpdate["id"]?.ToString() ?? throw new InvalidOperationException());
    var updatedProduct = await client.Data.UpdateAsync("products", productId, new Dictionary<string, object?>
    {
        ["quantity"] = 999
    });
    Console.WriteLine($"   ✓ Updated {updatedProduct["name"]}: quantity = {updatedProduct["quantity"]}");
    Console.WriteLine();

    // ============================================
    // 5. Delete Operation
    // ============================================
    Console.WriteLine("[5] Deleting a product...");

    var productToDelete = allProducts.Last();
    var deleteId = Guid.Parse(productToDelete["id"]?.ToString() ?? throw new InvalidOperationException());
    var deleted = await client.Data.DeleteAsync("products", deleteId);
    Console.WriteLine($"   ✓ Deleted: {deleted}");
    Console.WriteLine();

    // ============================================
    // 6. Bulk Operations
    // ============================================
    Console.WriteLine("[6] Bulk import products...");

    var bulkProducts = new List<Dictionary<string, object?>>();
    for (int i = 1; i <= 5; i++)
    {
        bulkProducts.Add(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["name"] = $"Bulk Product {i}",
            ["description"] = $"Description for bulk product {i}",
            ["price"] = 10.00m * i,
            ["quantity"] = 10 * i,
            ["is_active"] = true
        });
    }

    var bulkResult = await client.Bulk.ImportJsonAsync("products", bulkProducts);
    Console.WriteLine($"   ✓ Imported: {bulkResult.SuccessCount} products");
    Console.WriteLine();

    // ============================================
    // 7. Final count
    // ============================================
    Console.WriteLine("[7] Final product count...");
    var finalProducts = await client.Data.ListAsync("products");
    Console.WriteLine($"   Total products in database: {finalProducts.Count}");
    Console.WriteLine();

    // ============================================
    // 8. Cleanup (optional)
    // ============================================
    Console.WriteLine("[8] Cleaning up...");
    await client.Schema.DeleteTableAsync("products");
    Console.WriteLine("   ✓ Deleted 'products' table");
    Console.WriteLine();

    Console.WriteLine("===========================================");
    Console.WriteLine("  Sample completed successfully!");
    Console.WriteLine("===========================================");
}
catch (MorphDBException ex)
{
    Console.WriteLine();
    Console.WriteLine($"MorphDB Error: {ex.Message}");
    Console.WriteLine($"Status Code: {ex.StatusCode}");
    if (!string.IsNullOrEmpty(ex.ErrorCode))
    {
        Console.WriteLine($"Error Code: {ex.ErrorCode}");
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("Make sure MorphDB service is running at the configured URL.");
    Console.WriteLine("Start the service with: docker-compose --profile app up -d");
}
