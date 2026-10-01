namespace RetakeV4.Configuration;

public interface IConfigValidator<T> where T : ModuleConfig
{
    ValidationResult<T> Validate(T config, T defaults, string file);
}
