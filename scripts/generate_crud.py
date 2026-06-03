import sys
import os
import re
import subprocess

def parse_entities(filepath):
    with open(filepath, 'r') as f:
        content = f.read()
    
    # Find all classes
    class_pattern = re.compile(r'public class (\w+)')
    classes = class_pattern.findall(content)
    
    entities = {}
    for cls in classes:
        # Split by class name to find its block roughly
        parts = content.split(f"public class {cls}")
        if len(parts) > 1:
            block = parts[1].split('public class')[0] # just grab until the next class
            # Find properties
            prop_pattern = re.compile(r'public\s+([a-zA-Z0-9_\?<>\[\]]+)\s+(\w+)\s*{\s*get;\s*set;\s*}')
            props = prop_pattern.findall(block)
            entities[cls] = props
    return entities

def to_camel_case(snake_str):
    components = snake_str.split('_')
    return components[0] + ''.join(x.title() for x in components[1:])

def camel_case_first_lower(s):
    if not s:
        return s
    return s[0].lower() + s[1:]

def get_mock_value(prop_type, prop_name, suffix=""):
    prop_type = prop_type.replace('?', '')
    if prop_type == 'string':
        return f'"{prop_name} Test {suffix}"'
    elif prop_type in ('int', 'long'):
        return '1' if not suffix else '2'
    elif prop_type in ('decimal', 'float', 'double'):
        return '10.5' if not suffix else '20.5'
    elif prop_type == 'bool':
        return 'true' if not suffix else 'false'
    elif prop_type == 'DateTime':
        return 'DateTime.UtcNow'
    return 'null'

def run(): # NOSONAR
    entities_path = 'src/Database/Entities.cs'
    if not os.path.exists(entities_path):
        print(f"Error: {entities_path} not found.")
        sys.exit(1)

    entities = parse_entities(entities_path)
    
    entity_name = sys.argv[1] if len(sys.argv) > 1 else None
    
    if not entity_name:
        print("Available entities:")
        entity_list = list(entities.keys())
        for i, ent in enumerate(entity_list):
            print(f"{i + 1}. {ent}")
        choice = input("Select an entity by number (or type name): ")
        if choice.isdigit() and 1 <= int(choice) <= len(entity_list):
            entity_name = entity_list[int(choice) - 1]
        else:
            entity_name = choice

    if entity_name not in entities:
        print(f"Error: Entity '{entity_name}' not found in Entities.cs.")
        sys.exit(1)

    is_system_entity = entity_name.lower() in ['feature', 'audit', 'errorlog', 'error']
    
    feature_id = entity_name.lower()
    feature_name = entity_name
    feature_desc = f"Auto-generated CRUD for {entity_name}"
    
    if not is_system_entity:
        print("\n--- RBAC Configuration ---")
        register_rbac = input("Do you want to register this feature in RBAC (DbInitializer)? [Y/n]: ").strip().lower()
        if register_rbac != 'n':
            in_id = input(f"Feature ID (default: {feature_id}): ").strip()
            if in_id: feature_id = in_id
            
            in_name = input(f"Feature Name (default: {feature_name}): ").strip()
            if in_name: feature_name = in_name
            
            in_desc = input(f"Feature Description (default: {feature_desc}): ").strip()
            if in_desc: feature_desc = in_desc
        else:
            is_system_entity = True

    props = entities[entity_name]
    
    # Fields to exclude from Create/Update DTOs
    excluded_fields = {'Id', 'Active', 'IsDeleted', 'DeletedAt', 'CreatedAt', 'UpdatedAt', 'IdAuth', 'Auth', 'IdRole', 'Role'}
    
    create_dto_props = []
    response_dto_props = []
    create_mappings = []
    update_mappings = []
    response_mappings = []
    validation_rules = []
    mock_payload_create = []
    mock_payload_update = []
    allowed_fields = []
    
    for p_type, p_name in props:
        # Avoid foreign key virtuals in DTOs directly for simple CRUD
        if "virtual" in p_type or p_type in entities:
            continue
            
        allowed_fields.append(f'"{p_name.lower()}"')
        
        # Response DTO
        if p_name == 'Id':
            response_dto_props.append('            public string Id { get; init; } = string.Empty;')
        elif p_name in ('Active', 'IsDeleted', 'CreatedAt', 'UpdatedAt', 'DeletedAt'):
            json_prop = p_name
            if p_name == 'IsDeleted': json_prop = 'is_deleted'
            if p_name == 'CreatedAt': json_prop = 'created_at'
            if p_name == 'UpdatedAt': json_prop = 'updated_at'
            if p_name == 'DeletedAt': json_prop = 'deleted_at'
            
            if json_prop != p_name and p_name != 'Active':
                response_dto_props.append(f'            [JsonPropertyName("{json_prop}")]\n            public {p_type} {p_name} {{ get; init; }}')
            else:
                response_dto_props.append(f'            public {p_type} {p_name} {{ get; init; }}')
        else:
            response_dto_props.append(f'            public {p_type} {p_name} {{ get; init; }}' + (' = string.Empty;' if p_type == 'string' else ''))
            response_mappings.append(f'                {p_name} = entity.{p_name}')

            if p_name not in excluded_fields:
                create_dto_props.append(f'            public {p_type} {p_name} {{ get; init; }}' + (' = string.Empty;' if p_type == 'string' else ''))
                create_mappings.append(f'                {p_name} = dto.{p_name}')
                
                if p_type == 'string':
                    update_mappings.append(f'            if (!string.IsNullOrEmpty(dto.{p_name})) entity.{p_name} = dto.{p_name};')
                else:
                    update_mappings.append(f'            entity.{p_name} = dto.{p_name};')
                    
                if p_type == 'string' and '?' not in p_type:
                    validation_rules.append(f'            RuleFor(x => x.{p_name}).NotEmpty().WithMessage("{p_name} is required.");')
                
                # Mock payloads
                json_key = camel_case_first_lower(p_name)
                mock_payload_create.append(f'                {{ "{json_key}", {get_mock_value(p_type, p_name)} }}')
                mock_payload_update.append(f'                {{ "{json_key}", {get_mock_value(p_type, p_name, "Updated")} }}')

    allowed_fields.extend(['"active"', '"created_at"', '"updated_at"'])
    allowed_fields = list(set(allowed_fields))

    replacements = {
        '{{EntityName}}': entity_name,
        '{{EntityNameLower}}': entity_name.lower(),
        '{{FeatureId}}': feature_id,
        '{{AllowedFields}}': ', '.join(allowed_fields),
        '{{CreateDtoProperties}}': '\n'.join(create_dto_props),
        '{{ResponseDtoProperties}}': '\n'.join(response_dto_props),
        '{{CreateMappings}}': ',\n'.join(create_mappings),
        '{{UpdateMappings}}': '\n'.join(update_mappings),
        '{{ResponseMappings}}': ',\n'.join(response_mappings),
        '{{ValidationRules}}': '\n'.join(validation_rules),
        '{{MockPayloadCreate}}': ',\n'.join(mock_payload_create),
        '{{MockPayloadUpdate}}': ',\n'.join(mock_payload_update),
    }

    def process_template(tpl_path, out_path):
        with open(tpl_path, 'r') as f:
            content = f.read()
        for k, v in replacements.items():
            content = content.replace(k, v)
            
        if is_system_entity:
            content = re.sub(r'\[CheckPermission\([^\]]+\)\]', '[AuthorizeAdmin]', content)
            
        os.makedirs(os.path.dirname(out_path), exist_ok=True)
        with open(out_path, 'w') as f:
            f.write(content)
        print(f"Created: {out_path}")

    # Generate Controller
    ctrl_tpl = 'scripts/templates/Controller.cs.tpl'
    ctrl_out = f'src/Features/{entity_name}/{entity_name}Controller.cs'
    process_template(ctrl_tpl, ctrl_out)

    # Generate Tests
    test_tpl = 'scripts/templates/IntegrationTest.cs.tpl'
    test_out = f'tests/Tests/{entity_name}Tests.cs'
    process_template(test_tpl, test_out)

    # Seed DbInitializer (only for non-system entities)
    if not is_system_entity:
        db_init_path = 'src/Database/DbInitializer.cs'
        with open(db_init_path, 'r') as f:
            db_init_content = f.read()
            
        feature_seed = f'                    new Feature {{ Id = "{feature_id}", Name = "{feature_name}", Description = "{feature_desc}" }}'
        if feature_seed not in db_init_content:
            # Find the features array and insert
            pattern = re.compile(r'(var features = new\[\]\s*{)([\s\S]*?)(};)')
            match = pattern.search(db_init_content)
            if match:
                existing = match.group(2)
                if not existing.strip().endswith(','):
                    existing = existing.rstrip() + ','
                new_block = match.group(1) + existing + '\n' + feature_seed + '\n                ' + match.group(3)
                db_init_content = db_init_content[:match.start()] + new_block + db_init_content[match.end():]
                with open(db_init_path, 'w') as f:
                    f.write(db_init_content)
                print("Injected feature into DbInitializer.cs")

    # Update DbContext
    db_context_path = 'src/Database/ApplicationDbContext.cs'
    with open(db_context_path, 'r') as f:
        db_ctx_content = f.read()

    db_set_line = f'        public DbSet<{entity_name}> {entity_name} {{ get; set; }} = null!;'
    if db_set_line not in db_ctx_content:
        db_ctx_content = re.sub(
            r'(\s+protected override void OnModelCreating)',
            r'\n' + db_set_line + r'\1',
            db_ctx_content,
            count=1
        )
        
    to_table_line = f'            modelBuilder.Entity<{entity_name}>().ToTable("{entity_name}");'
    if to_table_line not in db_ctx_content:
        db_ctx_content = re.sub(
            r'(\s+/\* Audit tables map to "audit" schema \*/)',
            r'\n' + to_table_line + r'\1',
            db_ctx_content,
            count=1
        )
        
    with open(db_context_path, 'w') as f:
        f.write(db_ctx_content)
    print("Injected DbSet and ToTable into ApplicationDbContext.cs")

    print(f"\n✅ CRUD for {entity_name} generated successfully!")
    
    # Prompt for migration
    run_migration = input(f"\nDo you want to create and apply an EF Core migration for {entity_name}? (y/N): ")
    if run_migration.lower() == 'y':
        print("\nRunning migration...")
        os.system(f"dotnet ef migrations add Add{entity_name}Table -p src/MageBackend.csproj")
        os.system("dotnet ef database update -p src/MageBackend.csproj")
        print("Migration applied!")

if __name__ == '__main__':
    run()
