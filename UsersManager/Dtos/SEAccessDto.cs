namespace UsersManager.Dtos;

internal record SEAccessListDto(int Count, List<SEAccessDto> Results);
internal record SEAccessDto(int Id, AccessUserDto UserDto, AccessCustomerDto CustomerDto);
internal record AccessCustomerDto(int Id, string Name);
internal record AccessUserDto(int Id, string Username);