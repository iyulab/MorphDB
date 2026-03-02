# CheckExpression 논리명→물리명 변환 + PostgresException 핸들링 구현 계획

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** CheckExpression 내 논리 컬럼명을 물리명으로 변환하여 DDL이 정상 실행되게 하고, PostgresException을 적절히 처리하여 500 대신 400 응답을 반환한다.

**Architecture:** CheckExpression 변환은 `PostgresSchemaManager`에서 DDL 생성 직전에 수행한다. 이미 `FormulaSqlTranslator`에 동일한 논리명→물리명 패턴이 존재하므로, `DdlBuilder`에 정적 헬퍼 메서드를 추가하여 재사용한다. PostgresException은 Npgsql 레이어(`PostgresSchemaManager`)에서 catch하여 `SchemaException`으로 변환한다 — Service 레이어가 Npgsql 타입을 알 필요 없도록.

**Tech Stack:** C# / .NET 10, Npgsql, xUnit, FluentAssertions

---

## Task 1: DdlBuilder에 CheckExpression 변환 헬퍼 추가

**Files:**
- Modify: `src/MorphDB.Npgsql/Ddl/DdlBuilder.cs` (ColumnDefinition 클래스 내)
- Test: `tests/MorphDB.Tests/Unit/DdlBuilderTests.cs`

### Step 1: Failing 테스트 작성

`DdlBuilderTests.cs` 끝에 추가:

```csharp
[Fact]
public void TranslateCheckExpression_ShouldReplaceLogicalWithPhysicalNames()
{
    // Arrange
    var columnMappings = new Dictionary<string, string>
    {
        ["quantity"] = "col_a1b2c3d4e5f6",
        ["price"] = "col_f6e5d4c3b2a1"
    };

    // Act
    var result = DdlBuilder.TranslateCheckExpression("quantity >= 0 AND price > 0", columnMappings);

    // Assert
    result.Should().Be("\"col_a1b2c3d4e5f6\" >= 0 AND \"col_f6e5d4c3b2a1\" > 0");
}

[Fact]
public void TranslateCheckExpression_ShouldNotReplacePartialMatches()
{
    // "price" should not match "unit_price"
    var columnMappings = new Dictionary<string, string>
    {
        ["price"] = "col_aaa",
        ["unit_price"] = "col_bbb"
    };

    var result = DdlBuilder.TranslateCheckExpression("unit_price > 0 AND price < 10000", columnMappings);

    result.Should().Be("\"col_bbb\" > 0 AND \"col_aaa\" < 10000");
}

[Fact]
public void TranslateCheckExpression_WithNullOrEmpty_ShouldReturnAsIs()
{
    var mappings = new Dictionary<string, string> { ["x"] = "col_x" };

    DdlBuilder.TranslateCheckExpression(null, mappings).Should().BeNull();
    DdlBuilder.TranslateCheckExpression("", mappings).Should().Be("");
}
```

### Step 2: 테스트 실패 확인

Run: `dotnet test tests/MorphDB.Tests --filter "TranslateCheckExpression" --no-build 2>&1 | head -20`
Expected: 컴파일 에러 — `TranslateCheckExpression` 메서드가 존재하지 않음

### Step 3: 구현

`src/MorphDB.Npgsql/Ddl/DdlBuilder.cs`의 `DdlBuilder` 클래스에 정적 메서드 추가:

```csharp
/// <summary>
/// Translates logical column names in a CHECK expression to their physical names.
/// Uses word-boundary matching to avoid partial replacements (e.g., "price" won't match "unit_price").
/// </summary>
public static string? TranslateCheckExpression(
    string? checkExpression,
    IReadOnlyDictionary<string, string> logicalToPhysicalMap)
{
    if (string.IsNullOrEmpty(checkExpression))
        return checkExpression;

    var result = checkExpression;

    // Sort by length descending to replace longer names first (e.g., "unit_price" before "price")
    foreach (var (logicalName, physicalName) in logicalToPhysicalMap.OrderByDescending(kv => kv.Key.Length))
    {
        var pattern = $@"\b{Regex.Escape(logicalName)}\b";
        result = Regex.Replace(result, pattern, QuoteIdentifier(physicalName));
    }

    return result;
}
```

### Step 4: 테스트 통과 확인

Run: `dotnet test tests/MorphDB.Tests --filter "TranslateCheckExpression" -v minimal`
Expected: 3개 테스트 PASS

### Step 5: 커밋

```bash
git add src/MorphDB.Npgsql/Ddl/DdlBuilder.cs tests/MorphDB.Tests/Unit/DdlBuilderTests.cs
git commit -m "feat: add DdlBuilder.TranslateCheckExpression for logical-to-physical name mapping"
```

---

## Task 2: PostgresSchemaManager에서 CheckExpression 변환 적용

**Files:**
- Modify: `src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs` (CreateTableAsync, AddColumnAsync)

### Step 1: `CreateTableAsync` 수정

`PostgresSchemaManager.cs`의 `CreateTableAsync()` 메서드에서, 사용자 정의 컬럼의 `columnDefinitions`를 생성한 **직후** (DDL 실행 직전), CheckExpression을 변환한다.

현재 코드 (`// Execute DDL and insert metadata in a transaction` 바로 위):
```csharp
// Add user-defined columns
foreach (var colReq in request.Columns)
{
    ...
    columns.Add(column);
    if (!isVirtualColumn)
    {
        columnDefinitions.Add(ColumnDefinition.FromMetadata(column));
    }
}
```

변경: user-defined column 루프 끝 이후, DDL 실행 전에 변환 로직 삽입:

```csharp
// Translate logical names in CHECK expressions to physical names
var logicalToPhysicalMap = columns
    .Where(c => !c.PhysicalName.StartsWith("virtual_"))
    .ToDictionary(c => c.LogicalName, c => c.PhysicalName);

for (var i = 0; i < columnDefinitions.Count; i++)
{
    if (columnDefinitions[i].CheckExpression is not null)
    {
        columnDefinitions[i] = columnDefinitions[i] with
        {
            CheckExpression = DdlBuilder.TranslateCheckExpression(
                columnDefinitions[i].CheckExpression, logicalToPhysicalMap)
        };
    }
}
```

### Step 2: `AddColumnAsync` 수정

`AddColumnAsync()`에서 `var columnDef = ColumnDefinition.FromMetadata(column);` 바로 다음에:

```csharp
// Translate logical names in CHECK expression to physical names
if (columnDef.CheckExpression is not null)
{
    var logicalToPhysicalMap = table.Columns
        .Where(c => c.IsActive && !c.PhysicalName.StartsWith("virtual_"))
        .ToDictionary(c => c.LogicalName, c => c.PhysicalName);
    logicalToPhysicalMap[column.LogicalName] = column.PhysicalName;

    columnDef = columnDef with
    {
        CheckExpression = DdlBuilder.TranslateCheckExpression(
            columnDef.CheckExpression, logicalToPhysicalMap)
    };
}
```

### Step 3: 빌드 확인

Run: `dotnet build src/MorphDB.Npgsql`
Expected: Build succeeded

### Step 4: 커밋

```bash
git add src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs
git commit -m "fix: translate logical column names in CheckExpression to physical names before DDL"
```

---

## Task 3: PostgresException → SchemaException 변환

**Files:**
- Modify: `src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs` (CreateTableAsync, AddColumnAsync catch 블록)
- Modify: `src/MorphDB.Service/Controllers/SchemaController.cs` (SchemaException catch 추가)

### Step 1: `PostgresSchemaManager` catch 블록 개선

`CreateTableAsync()`의 기존 catch 블록:
```csharp
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

변경:
```csharp
catch (Npgsql.PostgresException ex)
{
    await transaction.RollbackAsync(cancellationToken);
    throw new SchemaException($"DDL execution failed: {ex.MessageText}", ex);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

`AddColumnAsync()`에도 동일하게 적용.

### Step 2: SchemaController에 SchemaException catch 추가

`SchemaController.CreateTable()`에서 기존 `catch (ValidationException)` 블록 뒤에:

```csharp
catch (SchemaException ex)
{
    return BadRequest(new ErrorResponse
    {
        Error = "SchemaError",
        Message = ex.Message
    });
}
```

`SchemaController.AddColumn()`에도 동일하게 추가.

### Step 3: 빌드 확인

Run: `dotnet build src/MorphDB.Service`
Expected: Build succeeded

### Step 4: 커밋

```bash
git add src/MorphDB.Npgsql/Services/PostgresSchemaManager.cs src/MorphDB.Service/Controllers/SchemaController.cs
git commit -m "fix: catch PostgresException and return 400 instead of 500 for schema DDL errors"
```

---

## Task 4: 기존 DdlBuilder 테스트 보강 (물리명 시나리오)

**Files:**
- Modify: `tests/MorphDB.Tests/Unit/DdlBuilderTests.cs`

### Step 1: 물리명 + CHECK 시나리오 테스트 추가

기존 `BuildCreateTable_WithCheckConstraint_ShouldGenerateValidSql` 은 `PhysicalName = "age"`로 테스트하여 논리명=물리명인 케이스. 해시된 물리명 시나리오 추가:

```csharp
[Fact]
public void BuildCreateTable_WithHashedPhysicalNameAndCheck_ShouldUsePhysicalNameInCheck()
{
    // Arrange — simulates translated check expression with physical names
    var columns = new List<ColumnDefinition>
    {
        new()
        {
            PhysicalName = "col_a1b2c3d4e5f6",
            NativeType = "INTEGER",
            IsNullable = false,
            CheckExpression = "\"col_a1b2c3d4e5f6\" >= 0"
        }
    };

    // Act
    var sql = DdlBuilder.BuildCreateTable("t_test", columns);

    // Assert
    sql.Should().Contain("CHECK (\"col_a1b2c3d4e5f6\" >= 0)");
}
```

### Step 2: 테스트 통과 확인

Run: `dotnet test tests/MorphDB.Tests --filter "DdlBuilderTests" -v minimal`
Expected: 모든 DdlBuilder 테스트 PASS

### Step 3: 커밋

```bash
git add tests/MorphDB.Tests/Unit/DdlBuilderTests.cs
git commit -m "test: add DdlBuilder tests for hashed physical name CHECK expressions"
```

---

## Task 5: 전체 테스트 실행 및 최종 검증

### Step 1: 전체 단위 테스트 실행

Run: `dotnet test tests/MorphDB.Tests --filter "Unit" -v minimal`
Expected: 모든 단위 테스트 PASS

### Step 2: 빌드 경고 확인

Run: `dotnet build -warnaserror 2>&1 | tail -5`
Expected: Build succeeded, 0 warnings

### Step 3: 최종 커밋 (필요 시)

정리가 필요하면 추가 커밋.
