namespace UsersManager.Dtos;

internal record SECustomerListDto(int Count, List<SECustomerDto> Results);
internal record SECustomerDto(int Id, string Name);
//nip, address, city, postcode, country, baan