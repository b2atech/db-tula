# db-tula

## Overview
**db-tula** is a simple and intuitive schema comparison tool designed to compare database schemas, specifically for PostgreSQL databases. It helps in identifying differences between two database schemas, ensuring consistency and aiding in database migrations.

## Features
- Compare tables and columns between two PostgreSQL databases.
- Highlight missing tables and columns.
- Detect differences in data types.
- User-friendly interface built with WPF.
- Easy to configure and use.

## Installation

### Prerequisites
- [.NET Core SDK](https://dotnet.microsoft.com/download) (version X.X or later)
- [PostgreSQL](https://www.postgresql.org/download/)
- [Npgsql](https://www.nuget.org/packages/Npgsql/)

### Steps
1. **Clone the repository**
    ```sh
    git clone https://github.com/b2atech/db-tula.git
    cd db-tula
    ```

2. **Install dependencies**
    ```sh
    dotnet restore
    ```

3. **Build the project**
    ```sh
    dotnet build
    ```

4. **Run the application**
    ```sh
    dotnet run
    ```

## Usage

1. **Open the application**: Launch the WPF application by running `db-tula.exe` or using `dotnet run`.

2. **Enter connection strings**:
    - **Source Database Connection String**: Provide the connection string for the source PostgreSQL database.
    - **Target Database Connection String**: Provide the connection string for the target PostgreSQL database.

3. **Compare Schemas**: Click the `Compare Schemas` button to start the comparison process.

4. **View Differences**: The differences between the schemas will be displayed in the list box, highlighting missing tables, columns, and data type mismatches.

## Contributing

Contributions are welcome! Please follow these steps:

1. **Fork the repository**.
2. **Create a new branch**.
    ```sh
    git checkout -b feature-branch
    ```

3. **Make your changes**.
4. **Commit your changes**.
    ```sh
    git commit -m "Description of your changes"
    ```

5. **Push to the branch**.
    ```sh
    git push origin feature-branch
    ```

6. **Create a Pull Request**.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE.md) file for more details.

## Contact

For any questions or suggestions, feel free to open an issue or reach out to me at [bharat.mane@gmail.com](mailto:bharat.mane@gmail.com).

---

**db-tula** - Simplifying database schema comparison.
