import os
import sys
import subprocess

def run():
    print("\n--- MageBackend Storage Provider Generator ---")
    print("Which Storage Provider do you want to implement?")
    print("  [1] Local Storage (Default)")
    print("  [2] AWS S3")
    print("  [3] Google Cloud Storage")
    print("  [4] Azure Blob Storage")
    
    choice = input("Enter choice [1-4]: ").strip()
    
    provider_class = ""
    package = ""
    template_name = ""
    test_template_name = ""

    if choice == "1":
        print("Local Storage is already the default. Nothing to do!")
        sys.exit(0)
    elif choice == "2":
        provider_class = "S3StorageProvider"
        package = "AWSSDK.S3"
        template_name = "S3StorageProvider.cs.tpl"
        test_template_name = "S3StorageProviderTests.cs.tpl"
    elif choice == "3":
        provider_class = "GcsStorageProvider"
        package = "Google.Cloud.Storage.V1"
        template_name = "GcsStorageProvider.cs.tpl"
        test_template_name = "GcsStorageProviderTests.cs.tpl"
    elif choice == "4":
        provider_class = "AzureBlobStorageProvider"
        package = "Azure.Storage.Blobs"
        template_name = "AzureBlobStorageProvider.cs.tpl"
        test_template_name = "AzureBlobStorageProviderTests.cs.tpl"
    else:
        print("Invalid choice.")
        sys.exit(1)

    template_path = os.path.join(os.path.dirname(__file__), "templates", template_name)
    test_template_path = os.path.join(os.path.dirname(__file__), "templates", test_template_name)
    
    if not os.path.exists(template_path) or not os.path.exists(test_template_path):
        print(f"❌ Template not found: {template_path} or {test_template_path}")
        sys.exit(1)

    with open(template_path, 'r') as f:
        provider_code = f.read()

    with open(test_template_path, 'r') as f:
        test_code = f.read()

    print(f"\nInstalling {package}...")
    subprocess.run(["dotnet", "add", "src/MageBackend.csproj", "package", package], check=True)

    provider_path = f"src/Infrastructure/Storage/{provider_class}.cs"
    with open(provider_path, 'w') as f:
        f.write(provider_code)
    print(f"Created: {provider_path}")

    test_path = f"tests/Tests/{provider_class}Tests.cs"
    with open(test_path, 'w') as f:
        f.write(test_code)
    print(f"Created: {test_path}")

    # Update Program.cs
    program_path = "src/Program.cs"
    with open(program_path, 'r') as f:
        content = f.read()
    
    import re
    content = re.sub(
        r"builder\.Services\.AddSingleton<IStorageProvider, \w+>\(\);",
        f"builder.Services.AddSingleton<IStorageProvider, {provider_class}>();",
        content
    )

    with open(program_path, 'w') as f:
        f.write(content)
        
    print(f"Updated: {program_path} -> Registered {provider_class}")
    print("\n✅ Storage provider generated successfully!")
    print("Don't forget to update your .env with the appropriate credentials.")

if __name__ == "__main__":
    run()
