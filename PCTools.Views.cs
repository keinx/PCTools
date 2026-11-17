// XAML 常量集中在这里，PCTools.cs 保留可执行逻辑。

namespace PCTools
{
    // ------------------------------------------------------------------ XAML
    internal static partial class Views
    {
        // 这些 XAML 只在运行时传给 XamlReader.Parse。用 static readonly 而不是 const：
        // const 会被编译器在常量表和每处引用各存一份 UTF-16 副本，单这一项就多出约 260KB。
        // 细滚动条：主窗口与所有弹窗共用。原来只写在 Views.Main 的 Grid.Resources 里，
        // 弹窗的视觉树拿不到，就退回系统默认的 17px 带箭头样式
        internal static readonly string ScrollSkin = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='ThinThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='tb' CornerRadius='3' Margin='3,3' Background='{DynamicResource ScrollThumb}'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='tb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
            </Trigger>
            <Trigger Property='IsDragging' Value='True'>
              <Setter TargetName='tb' Property='Background' Value='{DynamicResource ScrollThumbHover}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='12'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='true' Focusable='False'
                   Orientation='{TemplateBinding Orientation}'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'
                              Opacity='0' OverridesDefaultStyle='True'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource ThinThumb}'/>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'
                              Opacity='0' OverridesDefaultStyle='True'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Orientation' Value='Horizontal'>
              <Setter Property='Width' Value='Auto'/>
              <Setter Property='Height' Value='12'/>
              <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
        // All XAML attributes use single quotes so the C# verbatim string needs no escaping.
        internal static readonly string Main = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
      xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
      x:Name='Root' Background='{DynamicResource AppBg}'>
  <Grid.Resources>
    <!-- 工具条图标几何串集中为资源：四个页面的同名按钮不再各存一份 -->
    <PathGeometry x:Key='IconCheck' Figures='M 1,5.5 L 4.2,8.8 L 10.5,2'/>
    <PathGeometry x:Key='IconDisable' Figures='M 5.75,1 A 4.75,4.75 0 1 0 5.75,10.5 A 4.75,4.75 0 1 0 5.75,1 M 2.4,9 L 9.1,2.3'/>
    <PathGeometry x:Key='IconRefresh' Figures='M 10,5.75 A 4.4,4.4 0 1 1 5.75,1.2 M 5.75,1.2 L 8.3,1.2 M 5.75,1.2 L 5.75,3.7'/>
    <PathGeometry x:Key='IconSparkle' Figures='M 5.75,0.8 L 7,4.5 L 10.7,5.75 L 7,7 L 5.75,10.7 L 4.5,7 L 0.8,5.75 L 4.5,4.5 Z'/>
    <Style x:Key='TextCell' TargetType='TextBlock'>
      <Setter Property='TextTrimming' Value='CharacterEllipsis'/>
      <Setter Property='VerticalAlignment' Value='Center'/>
      <Setter Property='ToolTip' Value='{Binding RelativeSource={RelativeSource Self}, Path=Text}'/>
    </Style>
    <Style x:Key='SubCell' TargetType='TextBlock' BasedOn='{StaticResource TextCell}'>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='FontSize' Value='11.5'/>
    </Style>
    <Style x:Key='CmdBtn' TargetType='Button'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Height' Value='32'/>
      <Setter Property='Padding' Value='11,0'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='5' Background='{TemplateBinding Background}'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtn' TargetType='Button'>
      <Setter Property='Width' Value='46'/>
<Setter Property='Height' Value='48'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource WinBtnHover}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtnSmall' TargetType='Button'>
      <Setter Property='Width' Value='36'/>
      <Setter Property='Height' Value='32'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='5' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='WinBtnClose' TargetType='Button' BasedOn='{StaticResource WinBtn}'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' Background='Transparent'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource CloseHover}'/>
                <Setter Property='Foreground' Value='#FFFFFF'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='NavItem' TargetType='ListBoxItem'>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ListBoxItem'>
            <Grid Margin='6,1'>
              <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='10,9'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                  VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='ind' Width='3' Height='18' CornerRadius='1.5' HorizontalAlignment='Left'
                      VerticalAlignment='Center' Background='Transparent' Margin='2,0,0,0'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsSelected' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                <Setter TargetName='ind' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='NavButton' TargetType='Button'>
      <Setter Property='Padding' Value='10,9'/>
      <Setter Property='Margin' Value='6,1'/>
      <Setter Property='HorizontalAlignment' Value='Stretch'/>
      <Setter Property='HorizontalContentAlignment' Value='Left'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='SearchBox' TargetType='TextBox'>
      <Setter Property='Height' Value='32'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border x:Name='bd' CornerRadius='6' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34' Padding='10,0'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='*'/>
                </Grid.ColumnDefinitions>
                <Path Grid.Column='0' Width='13' Height='13' Margin='0,0,8,0' Stretch='Uniform'
                      Stroke='{DynamicResource TextSecondary}' StrokeThickness='1.4'
                      Data='M 5,1 A 4,4 0 1 0 5,9 A 4,4 0 1 0 5,1 M 8,8 L 12,12'/>
                <ScrollViewer Grid.Column='1' x:Name='PART_ContentHost' VerticalAlignment='Center'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsFocused' Value='True'>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGrid'>
      <Setter Property='Background' Value='{DynamicResource ContentBg}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='RowHeight' Value='38'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='RowBackground' Value='Transparent'/>
      <Setter Property='AlternatingRowBackground' Value='Transparent'/>
      <Setter Property='GridLinesVisibility' Value='None'/>
      <Setter Property='HeadersVisibility' Value='Column'/>
      <Setter Property='CanUserAddRows' Value='False'/>
      <Setter Property='CanUserDeleteRows' Value='False'/>
      <Setter Property='CanUserResizeRows' Value='False'/>
      <Setter Property='CanUserSortColumns' Value='True'/>
      <Setter Property='CanUserResizeColumns' Value='True'/>
      <Setter Property='IsReadOnly' Value='True'/>
      <Setter Property='AutoGenerateColumns' Value='False'/>
      <Setter Property='SelectionMode' Value='Extended'/>
      <Setter Property='SelectionUnit' Value='FullRow'/>
      <Setter Property='EnableRowVirtualization' Value='True'/>
      <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Auto'/>
    </Style>
    <Style x:Key='ColGripper' TargetType='Thumb'>
      <Setter Property='Width' Value='7'/>
      <Setter Property='Cursor' Value='SizeWE'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Thumb'>
            <Border Background='Transparent'/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridColumnHeader'>
      <Setter Property='Background' Value='{DynamicResource HeaderBg}'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='FontSize' Value='11.5'/>
<Setter Property='Height' Value='36'/>
      <Setter Property='Padding' Value='14,0'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='HorizontalContentAlignment' Value='Left'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='DataGridColumnHeader'>
            <Border x:Name='hd' Background='{TemplateBinding Background}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,0,0,1'
                    SnapsToDevicePixels='True'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter Grid.Column='0' Margin='{TemplateBinding Padding}'
                                  VerticalAlignment='Center'
                                  HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
                <Path x:Name='arrow' Grid.Column='1' Width='8' Height='4.5' Stretch='Fill'
                      Margin='0,0,4,0' VerticalAlignment='Center'
                      Fill='{DynamicResource Accent}' Visibility='Collapsed'
                      Data='M 0,4.5 L 4,0 L 8,4.5 Z'/>
                <Thumb x:Name='PART_LeftHeaderGripper' Style='{StaticResource ColGripper}'
                       HorizontalAlignment='Left'/>
                <Thumb x:Name='PART_RightHeaderGripper' Style='{StaticResource ColGripper}'
                       HorizontalAlignment='Right'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='hd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='SortDirection' Value='Ascending'>
                <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
              </Trigger>
              <Trigger Property='SortDirection' Value='Descending'>
                <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
                <Setter TargetName='arrow' Property='LayoutTransform'>
                  <Setter.Value>
                    <ScaleTransform ScaleY='-1'/>
                  </Setter.Value>
                </Setter>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridCell'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='Padding' Value='14,0'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <!-- 默认模板不使用 Padding，这里显式绑到 ContentPresenter，保证左右留白 -->
          <!-- BorderThickness 必须为 0：没有 BorderBrush 的边框不显示，却照样吃掉 2.68 DIP 内容宽度 -->
          <ControlTemplate TargetType='DataGridCell'>
            <Border Background='{TemplateBinding Background}' BorderThickness='0'
                    SnapsToDevicePixels='True'>
              <ContentPresenter Margin='{TemplateBinding Padding}'
                                VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                                SnapsToDevicePixels='True'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType='DataGridRow'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='DataGridRow'>
            <Grid Background='Transparent'>
              <Border x:Name='hl' Background='Transparent'/>
              <DataGridCellsPresenter ItemsPanel='{TemplateBinding ItemsPanel}' VerticalAlignment='Center'
                                      SnapsToDevicePixels='True'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='hl' Property='Background' Value='{DynamicResource RowHoverBg}'/>
              </Trigger>
              <Trigger Property='IsSelected' Value='True'>
                <Setter TargetName='hl' Property='Background' Value='{DynamicResource RowSelectedBg}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='StatusCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding StatusBgBrush}'>
        <TextBlock Text='{Binding Status}' Foreground='{Binding StatusBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='AiAdviceCell'>
      <Border x:Name='aiBd' CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding AiAdviceBgBrush}' ToolTip='云端 AI 给出的建议，点击右上角「更新AI建议」刷新'>
        <TextBlock Text='{Binding AiAdvice}' Foreground='{Binding AiAdviceBrush}' FontSize='11.5'/>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding='{Binding AiAdvice}' Value=''>
          <Setter TargetName='aiBd' Property='Visibility' Value='Collapsed'/>
        </DataTrigger>
        <DataTrigger Binding='{Binding AiAdvice}' Value='{x:Null}'>
          <Setter TargetName='aiBd' Property='Visibility' Value='Collapsed'/>
        </DataTrigger>
      </DataTemplate.Triggers>
    </DataTemplate>
    <DataTemplate x:Key='AdviceCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding AdviceBgBrush}' ToolTip='{Binding AdviceNote}'>
        <TextBlock Text='{Binding Advice}' Foreground='{Binding AdviceBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='StartTypeCell'>
      <Border CornerRadius='9' Padding='7,2' HorizontalAlignment='Left' VerticalAlignment='Center'
              Background='{Binding StartTypeBgBrush}'>
        <TextBlock Text='{Binding StartType}' Foreground='{Binding StartTypeBrush}' FontSize='11.5'/>
      </Border>
    </DataTemplate>
    <DataTemplate x:Key='StartupNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 2.5,5 A 1.5,1.5 0 0 1 4,3.5 L 12,3.5 A 1.5,1.5 0 0 1 13.5,5 L 13.5,12 A 1.5,1.5 0 0 1 12,13.5 L 4,13.5 A 1.5,1.5 0 0 1 2.5,12 Z M 2.5,6.6 L 13.5,6.6'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Name}'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='ServiceNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 8,3.7 A 4.3,4.3 0 1 1 8,12.3 A 4.3,4.3 0 1 1 8,3.7 M 8,6.2 A 1.8,1.8 0 1 1 8,9.8 A 1.8,1.8 0 1 1 8,6.2 M 12.3,8 L 14,8 M 10.15,4.28 L 11,2.8 M 5.85,4.28 L 5,2.8 M 3.7,8 L 2,8 M 5.85,11.72 L 5,13.2 M 10.15,11.72 L 11,13.2'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
    <DataTemplate x:Key='TaskNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 8,2 A 6,6 0 1 1 8,14 A 6,6 0 1 1 8,2 M 8,4.6 L 8,8.2 L 10.6,9.7'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
    <!-- 四页共用的空态模板：原来四个 StackPanel 逐字重复四份 -->
    <ControlTemplate x:Key='EmptyHint' TargetType='ContentControl'>
      <StackPanel>
        <Path Width='26' Height='26' Stretch='Uniform' Stroke='{DynamicResource BorderColor}'
              StrokeThickness='1.4' HorizontalAlignment='Center'
              Data='M 3,2 L 15,2 L 15,17 L 3,17 Z M 6,6 L 12,6 M 6,9.5 L 12,9.5 M 6,13 L 10,13'/>
        <TextBlock Text='没有匹配的项目' FontSize='12.5' Foreground='{DynamicResource TextSecondary}'
                   HorizontalAlignment='Center' Margin='0,10,0,0'/>
      </StackPanel>
    </ControlTemplate>
    <Style x:Key='CtxMenu' TargetType='ContextMenu'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='ContextMenu'>
            <Border CornerRadius='8' Background='{DynamicResource MenuBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1.34'
                    Padding='4' Margin='8'>
              <Border.Effect>
                <DropShadowEffect BlurRadius='16' ShadowDepth='3' Opacity='0.18' Color='#000000'/>
              </Border.Effect>
              <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Border>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='CtxItem' TargetType='MenuItem'>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='MenuItem'>
            <Border x:Name='bd' CornerRadius='5' Padding='10,7' Background='Transparent'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <ContentPresenter Grid.Column='0' ContentSource='Header' VerticalAlignment='Center'/>
                <TextBlock Grid.Column='1' Text='{TemplateBinding InputGestureText}' Margin='28,0,0,0'
                           Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsHighlighted' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.4'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='CtxSep' TargetType='Separator'>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Separator'>
            <Border Height='1' Background='{DynamicResource GridLine}' Margin='6,4'/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 页签：语义与左侧导航一致（选中底色 + 底部指示条 + 强调色文字） -->
    <Style x:Key='SegTab' TargetType='RadioButton'>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
      <Setter Property='Margin' Value='0,0,4,0'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='RadioButton'>
            <Grid>
              <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='14,7'>
                <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
              </Border>
              <Border x:Name='ind' Height='2' CornerRadius='1' VerticalAlignment='Bottom'
                      Margin='12,0,12,-1' Background='Transparent'/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsChecked' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                <Setter TargetName='ind' Property='Background' Value='{DynamicResource Accent}'/>
                <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
                <Setter Property='FontWeight' Value='SemiBold'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <DataTemplate x:Key='CtxNameCell'>
      <Grid VerticalAlignment='Center'>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='*'/>
        </Grid.ColumnDefinitions>
        <Path Width='13' Height='13' Stretch='Uniform' Stroke='{DynamicResource TextSecondary}'
              StrokeThickness='1.4' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
              Margin='0,0,9,0' VerticalAlignment='Center'
              Data='M 3.4,2.4 L 9.8,2.4 A 1.2,1.2 0 0 1 11,3.6 L 11,7.6 A 1.2,1.2 0 0 1 9.8,8.8 L 7,8.8 L 5.2,10.9 L 5.2,8.8 L 3.4,8.8 A 1.2,1.2 0 0 1 2.2,7.6 L 2.2,3.6 A 1.2,1.2 0 0 1 3.4,2.4 Z M 4.4,4.6 L 8.8,4.6 M 4.4,6.6 L 7.4,6.6'/>
        <TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' ToolTip='{Binding Detail}'/>
      </Grid>
    </DataTemplate>
  </Grid.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <!-- 标题栏 -->
    <Grid x:Name='TitleBar' Grid.Row='0' Background='{DynamicResource TitleBarBg}'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <StackPanel Grid.Column='0' Orientation='Horizontal' VerticalAlignment='Center' Margin='7,0,0,0'>
        <Button x:Name='BtnHamburger' Style='{StaticResource WinBtnSmall}' ToolTip='折叠 / 展开导航'>
          <Path Width='14' Height='12' Stretch='Fill'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1.4' Data='M 0,1 L 12,1 M 0,6 L 12,6 M 0,11 L 12,11'/>
        </Button>
        <TextBlock Text='系统项目管理' FontSize='12.5' FontWeight='SemiBold' Margin='6,0,0,0'
                   Foreground='{DynamicResource TextPrimary}' VerticalAlignment='Center'/>
      </StackPanel>
      <Grid Grid.Column='1' Width='340'>
        <TextBox x:Name='SearchBox' Style='{StaticResource SearchBox}' ToolTip='搜索当前列表'/>
        <TextBlock x:Name='SearchHint' Text='搜索名称、路径或关键字' FontSize='12' Margin='37,0,0,0'
                   Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'
                   IsHitTestVisible='False'/>
      </Grid>
      <StackPanel Grid.Column='2' Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
        <Button x:Name='BtnAdmin' Style='{StaticResource CmdBtn}' Margin='0,0,4,0'
                Content='以管理员身份重启' ToolTip='修改系统级项目需要管理员权限'/>
        <Button x:Name='BtnTheme' Style='{StaticResource WinBtn}' ToolTip='切换深色 / 浅色主题'>
          <Grid Width='12' Height='12'>
            <Grid x:Name='IconLight'>
              <Path Fill='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    Data='M 6,1 A 5,5 0 0 0 6,11 Z'/>
              <Path Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    StrokeThickness='1.2'
                    Data='M 6,1 A 5,5 0 1 1 6,11 A 5,5 0 1 1 6,1'/>
            </Grid>
            <Grid x:Name='IconDark' Visibility='Collapsed'>
              <Path Fill='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    Data='M 6,1 A 5,5 0 0 1 6,11 Z'/>
              <Path Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                    StrokeThickness='1.2'
                    Data='M 6,1 A 5,5 0 1 1 6,11 A 5,5 0 1 1 6,1'/>
            </Grid>
          </Grid>
        </Button>
        <Button x:Name='BtnMin' Style='{StaticResource WinBtn}' ToolTip='最小化'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1' Data='M 0.5,5 L 9.5,5'/>
        </Button>
        <Button x:Name='BtnMax' Style='{StaticResource WinBtn}' ToolTip='最大化 / 还原'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1' Data='M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z'/>
        </Button>
        <Button x:Name='BtnClose' Style='{StaticResource WinBtnClose}' ToolTip='关闭'>
          <Path Width='10' Height='10' Stretch='None' VerticalAlignment='Center'
                Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                StrokeThickness='1.1' Data='M 0.5,0.5 L 9.5,9.5 M 9.5,0.5 L 0.5,9.5'/>
        </Button>
      </StackPanel>
    </Grid>

    <!-- 主体 -->
    <Grid Grid.Row='1'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column='0' Background='{DynamicResource NavBg}'
              BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,0,1,0'>
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height='*'/>
            <RowDefinition Height='Auto'/>
          </Grid.RowDefinitions>
          <ListBox x:Name='Nav' Grid.Row='0' Width='212' BorderThickness='0' Padding='6,8'
                   Background='Transparent' ItemContainerStyle='{StaticResource NavItem}'
                   ScrollViewer.HorizontalScrollBarVisibility='Disabled'
                   SelectedIndex='0'>
            <ListBoxItem ToolTip='启动应用'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 4.0,2.0 L 12.0,2.0 A 2.0,2.0 0 0 1 14.0,4.0 L 14.0,12.0 A 2.0,2.0 0 0 1 12.0,14.0 L 4.0,14.0 A 2.0,2.0 0 0 1 2.0,12.0 L 2.0,4.0 A 2.0,2.0 0 0 1 4.0,2.0 Z M 8,11.0 L 8,5.6 M 6.0,7.6 L 8,5.6 L 10.0,7.6'/>
                <TextBlock Text='启动应用' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='服务'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 14.01,6.94 L 14.01,9.06 L 12.33,9.24 L 11.94,10.18 L 13.00,11.50 L 11.50,13.00 L 10.18,11.94 L 9.24,12.33 L 9.06,14.01 L 6.94,14.01 L 6.76,12.33 L 5.82,11.94 L 4.50,13.00 L 3.00,11.50 L 4.06,10.18 L 3.67,9.24 L 1.99,9.06 L 1.99,6.94 L 3.67,6.76 L 4.06,5.82 L 3.00,4.50 L 4.50,3.00 L 5.82,4.06 L 6.76,3.67 L 6.94,1.99 L 9.06,1.99 L 9.24,3.67 L 10.18,4.06 L 11.50,3.00 L 13.00,4.50 L 11.94,5.82 L 12.33,6.76 Z M 6.10,8 A 1.90,1.90 0 1 1 9.90,8 A 1.90,1.90 0 1 1 6.10,8'/>
                <TextBlock Text='服务管理' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='计划任务'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 2,8 A 6,6 0 1 1 14,8 A 6,6 0 1 1 2,8 M 8,4.2 L 8,8 L 11.0,9.75'/>
                <TextBlock Text='计划任务' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
            <ListBoxItem ToolTip='右键菜单'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}'
                      Data='M 2.2,3.8 L 2.2,11.6 L 4.4,9.4 L 5.8,12.6 L 7.2,12.0 L 5.9,9.0 L 8.4,8.9 Z M 10.6,3.8 L 14.0,3.8 M 10.6,6.4 L 14.0,6.4 M 10.6,9.0 L 14.0,9.0'/>
                <TextBlock Text='右键菜单' Margin='12,0,0,0' VerticalAlignment='Center' Tag='navtext'/>
              </StackPanel>
            </ListBoxItem>
          </ListBox>
          <Border Grid.Row='1' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0'
                  Padding='6,8,6,10'>
            <Button x:Name='BtnAiConfig' Style='{StaticResource NavButton}' ToolTip='配置 AI 接口与模型'>
              <StackPanel Orientation='Horizontal'>
                <Path Width='17' Height='17' Stretch='Uniform' Fill='Transparent' StrokeThickness='1.5'
                      StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                      Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                      Data='M 8,2 C 8.5,5.3 10.7,7.5 14,8 C 10.7,8.5 8.5,10.7 8,14 C 7.5,10.7 5.3,8.5 2,8 C 5.3,7.5 7.5,5.3 8,2 Z'/>
                <StackPanel x:Name='AiNavText' Margin='12,0,0,0' VerticalAlignment='Center'>
                  <TextBlock Text='配置 AI' Tag='navtext'/>
                </StackPanel>
              </StackPanel>
            </Button>
          </Border>
        </Grid>
      </Border>

      <Grid Grid.Column='1' Background='{DynamicResource ContentBg}'>
        <Grid.RowDefinitions>
          <RowDefinition Height='Auto'/>
          <RowDefinition Height='*'/>
        </Grid.RowDefinitions>

        <!-- 命令栏 -->
        <Grid Grid.Row='0' Margin='24,18,24,14'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <StackPanel Grid.Column='0' VerticalAlignment='Center'>
            <TextBlock x:Name='PageTitle' Text='启动应用' FontSize='20' FontWeight='SemiBold'
                       Foreground='{DynamicResource TextPrimary}'/>
            <TextBlock x:Name='PageSubtitle' Text='随 Windows 登录自动运行的注册表项与启动文件夹快捷方式'
                       FontSize='11.5' Foreground='{DynamicResource TextSecondary}' Margin='0,4,0,0'/>
          </StackPanel>
          <StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
            <StackPanel x:Name='CmdStartup' Orientation='Horizontal'>
              <Button x:Name='BtnStartupEnable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.7' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconCheck}'/>
                  <TextBlock Text='启用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnStartupDisable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconDisable}'/>
                  <TextBlock Text='禁用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnStartupRefresh' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconRefresh}'/>
                  <TextBlock Text='刷新' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnStartupAi' Style='{StaticResource CmdBtn}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.5' Margin='0,0,6,0' VerticalAlignment='Center'
                        Data='{StaticResource IconSparkle}'/>
                  <TextBlock Text='更新AI建议' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </StackPanel>
            <StackPanel x:Name='CmdService' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnServiceEnable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.7' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconCheck}'/>
                  <TextBlock Text='启用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnServiceDisable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconDisable}'/>
                  <TextBlock Text='禁用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnServiceStart' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='M 2.8,1.8 L 10,5.75 L 2.8,9.7 Z'/>
                  <TextBlock Text='启动' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnServiceStop' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='M 2.2,2.2 L 9.3,2.2 L 9.3,9.3 L 2.2,9.3 Z'/>
                  <TextBlock Text='停止' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnServiceRefresh' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconRefresh}'/>
                  <TextBlock Text='刷新' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnServiceAi' Style='{StaticResource CmdBtn}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.5' Margin='0,0,6,0' VerticalAlignment='Center'
                        Data='{StaticResource IconSparkle}'/>
                  <TextBlock Text='更新AI建议' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </StackPanel>
            <StackPanel x:Name='CmdTask' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnTaskEnable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.7' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconCheck}'/>
                  <TextBlock Text='启用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTaskDisable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconDisable}'/>
                  <TextBlock Text='禁用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTaskRun' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='M 2.8,1.8 L 10,5.75 L 2.8,9.7 Z'/>
                  <TextBlock Text='运行' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTaskStop' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='M 2.2,2.2 L 9.3,2.2 L 9.3,9.3 L 2.2,9.3 Z'/>
                  <TextBlock Text='停止' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTaskRefresh' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconRefresh}'/>
                  <TextBlock Text='刷新' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnTaskAi' Style='{StaticResource CmdBtn}' ToolTip='把当前列表的全部项目一次性发给云端 AI，结果保存在 PCToolsAI.sqlite'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.5' Margin='0,0,6,0' VerticalAlignment='Center'
                        Data='{StaticResource IconSparkle}'/>
                  <TextBlock Text='更新AI建议' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </StackPanel>
            <StackPanel x:Name='CmdCtx' Orientation='Horizontal' Visibility='Collapsed'>
              <Button x:Name='BtnCtxEnable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.7' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconCheck}'/>
                  <TextBlock Text='启用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnCtxDisable' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconDisable}'/>
                  <TextBlock Text='禁用' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnCtxRestart' Style='{StaticResource CmdBtn}' ToolTip='重启资源管理器使更改生效（任务栏会闪烁 1-2 秒）'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconRefresh}'/>
                  <TextBlock Text='重启资源管理器' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
              <Button x:Name='BtnCtxRefresh' Style='{StaticResource CmdBtn}'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='11' Height='11' Stretch='Uniform' Stroke='{Binding RelativeSource={RelativeSource AncestorType=Button}, Path=Foreground}'
                        StrokeThickness='1.6' Margin='0,0,6,0' VerticalAlignment='Center' Data='{StaticResource IconRefresh}'/>
                  <TextBlock Text='刷新' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </StackPanel>
          </StackPanel>
        </Grid>

        <!-- 列表 -->
        <Border Grid.Row='1' Background='Transparent' Margin='10,0,10,10'>
          <Grid>
            <Grid.RowDefinitions>
              <RowDefinition Height='Auto'/>
              <RowDefinition Height='*'/>
            </Grid.RowDefinitions>
            <!-- 统计条：四页共用一条，右侧是逐项 AI 分析入口 -->
            <Grid Grid.Row='0' Margin='2,0,0,6'>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width='*'/>
                <ColumnDefinition Width='Auto'/>
                <ColumnDefinition Width='Auto'/>
              </Grid.ColumnDefinitions>
              <TextBlock x:Name='ListSummary' Grid.Column='0' HorizontalAlignment='Right'
                         VerticalAlignment='Center' Margin='0,0,10,0' FontSize='11.5'
                         Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'/>
              <Border Grid.Column='1' Width='1' Height='12' Background='{DynamicResource BorderColor}'
                      VerticalAlignment='Center' Margin='0,0,10,0'/>
              <Button x:Name='BtnAnalyzeAll' Grid.Column='2' Style='{StaticResource CmdBtn}'
                      Height='26' Padding='9,0' FontSize='12'
                      ToolTip='逐项请求 AI 完整分析（项目介绍 + 处理建议），每项一次请求，消耗 Token 较多'>
                <StackPanel Orientation='Horizontal'>
                  <Path Width='10' Height='10' Stretch='Uniform' Stroke='{DynamicResource Accent}'
                        StrokeThickness='1.5' Fill='Transparent' Margin='0,0,5,0' VerticalAlignment='Center'
                        Data='{StaticResource IconSparkle}'/>
                  <TextBlock Text='更新AI分析' VerticalAlignment='Center'/>
                </StackPanel>
              </Button>
            </Grid>
            <Grid x:Name='PageStartup' Grid.Row='1'>
              <DataGrid x:Name='GridStartup' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='名称' Width='190' CellTemplate='{StaticResource StartupNameCell}' SortMemberPath='Name'/>
                  <DataGridTemplateColumn Header='状态' Width='92' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='内置建议' Width='92' CellTemplate='{StaticResource AdviceCell}' SortMemberPath='AdviceRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTextColumn Header='位置类型' Binding='{Binding Location}' Width='90' SortMemberPath='Location' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='作用范围' Binding='{Binding Scope}' Width='90' SortMemberPath='Scope' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='启动命令 / 目标路径' Binding='{Binding Command}' Width='270' MinWidth='160' SortMemberPath='Command' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyStartup' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageService' Grid.Row='1' Visibility='Collapsed'>
              <DataGrid x:Name='GridService' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='显示名称' Width='176' CellTemplate='{StaticResource ServiceNameCell}' SortMemberPath='Name'/>
                  <DataGridTextColumn Header='服务名称' Binding='{Binding ServiceName}' Width='110' SortMemberPath='ServiceName' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTemplateColumn Header='运行状态' Width='88' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='内置建议' Width='92' CellTemplate='{StaticResource AdviceCell}' SortMemberPath='AdviceRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTemplateColumn Header='启动类型' Width='80' CellTemplate='{StaticResource StartTypeCell}' SortMemberPath='StartTypeRank'/>
                  <DataGridTextColumn Header='服务类型' Binding='{Binding ServiceType}' Width='82' SortMemberPath='ServiceType' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='可执行路径' Binding='{Binding Path}' Width='210' MinWidth='150' SortMemberPath='Path' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyService' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageTask' Grid.Row='1' Visibility='Collapsed'>
              <DataGrid x:Name='GridTask' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                <DataGrid.Columns>
                  <DataGridTemplateColumn Header='任务名称' Width='188' CellTemplate='{StaticResource TaskNameCell}' SortMemberPath='Name'/>
                  <DataGridTemplateColumn Header='状态' Width='88' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                  <DataGridTemplateColumn Header='内置建议' Width='92' CellTemplate='{StaticResource AdviceCell}' SortMemberPath='AdviceRank'/>
                  <DataGridTemplateColumn Header='AI建议' Width='84' CellTemplate='{StaticResource AiAdviceCell}' SortMemberPath='AiAdviceRank'/>
                  <DataGridTextColumn Header='触发器' Binding='{Binding Trigger}' Width='126' SortMemberPath='Trigger' ElementStyle='{StaticResource TextCell}'/>
                  <DataGridTextColumn Header='执行操作' Binding='{Binding Action}' Width='216' MinWidth='160' SortMemberPath='Action' ElementStyle='{StaticResource SubCell}'/>
                  <DataGridTextColumn Header='上次运行' Binding='{Binding LastRun}' Width='96' SortMemberPath='LastRun' ElementStyle='{StaticResource SubCell}'/>
                </DataGrid.Columns>
              </DataGrid>
              <ContentControl x:Name='EmptyTask' HorizontalAlignment='Center' VerticalAlignment='Center'
                              Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
            </Grid>
            <Grid x:Name='PageCtx' Grid.Row='1' Visibility='Collapsed'>
              <Grid.RowDefinitions>
                <RowDefinition Height='Auto'/>
                <RowDefinition Height='*'/>
              </Grid.RowDefinitions>
              <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,10'>
                <RadioButton x:Name='TabCtxAll' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='全局' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxDesktop' GroupName='CtxTab' IsChecked='True'
                             Style='{StaticResource SegTab}' Content='桌面' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxDrive' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='驱动器' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxFile' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxFolder' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件夹' Margin='0,0,8,0'/>
                <RadioButton x:Name='TabCtxAfo' GroupName='CtxTab'
                             Style='{StaticResource SegTab}' Content='文件和文件夹'/>
              </StackPanel>
              <Grid Grid.Row='1'>
                <DataGrid x:Name='GridCtx' ScrollViewer.HorizontalScrollBarVisibility='Auto'>
                  <DataGrid.Columns>
                    <DataGridTemplateColumn Header='名称' Width='225' CellTemplate='{StaticResource CtxNameCell}' SortMemberPath='Name'/>
                    <DataGridTemplateColumn Header='状态' Width='92' CellTemplate='{StaticResource StatusCell}' SortMemberPath='StatusRank'/>
                    <DataGridTextColumn Header='类型' Binding='{Binding Kind}' Width='96' SortMemberPath='Kind' ElementStyle='{StaticResource TextCell}'/>
                    <DataGridTextColumn Header='作用域' Binding='{Binding Scope}' Width='104' MinWidth='88' SortMemberPath='Scope' ElementStyle='{StaticResource SubCell}'/>
                    <DataGridTextColumn Header='标识' Binding='{Binding Id}' Width='280' MinWidth='180' SortMemberPath='Id' ElementStyle='{StaticResource SubCell}'/>
                  </DataGrid.Columns>
                </DataGrid>
                <ContentControl x:Name='EmptyCtx' HorizontalAlignment='Center' VerticalAlignment='Center'
                                Visibility='Collapsed' Template='{StaticResource EmptyHint}'/>
              </Grid>
            </Grid>
          </Grid>
        </Border>
      </Grid>
    </Grid>

    <!-- 状态栏 -->
    <Border Grid.Row='2' Height='34' Background='{DynamicResource StatusBg}'
            BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0' Padding='24,0'>
      <Grid>
        <TextBlock x:Name='StatusText' Text='正在加载…' FontSize='11'
                   Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'
                   TextTrimming='CharacterEllipsis' Margin='0,0,12,0'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' VerticalAlignment='Center'>
          <TextBlock x:Name='CountText' FontSize='11'
                     Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
          <Border Width='1' Height='11' Background='{DynamicResource BorderColor}' Margin='10,0'/>
          <TextBlock Text='右键查看更多操作' FontSize='11'
                     Foreground='{DynamicResource TextSecondary}' VerticalAlignment='Center'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Grid>";

        // 五个弹窗（Prompt/AiSettings/AiResult/Confirm/Progress）共用的按钮样式。
        // 原先各窗口 XAML 自带一份近似副本，触发器与圆角已互相漂移，这里统一成一份
        // 超集（悬停/按下/禁用三态全带）；各窗口只针对真实差异（IsCancel/IsDefault/Margin）
        // 用 BasedOn 写几行覆盖，不再整段复制
        internal static readonly string BtnStyles = @"
    <Style x:Key='PBtn' TargetType='Button'>
      <Setter Property='Height' Value='36'/>
      <Setter Property='MinWidth' Value='96'/>
      <Setter Property='Padding' Value='18,0'/>
      <Setter Property='Margin' Value='10,0,0,0'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Cursor' Value='Hand'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='Background' Value='{DynamicResource BtnBg}'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <!-- 描边厚度用 1.34 DIP（约 4/3），不能改回 1：
                 1 DIP 在 150% 下是 1.5 设备像素，取整后落在半像素被抗锯齿吃掉；
                 4/3 DIP 在 150% 下是 2.000 设备像素，四边都能画成实线。
                 按钮高度也必须是 36 DIP（150% 下 54px，偶数）：34 DIP 是 51px 奇数，
                 起点半像素相位会让底部边框被挤成 0~1px，这才是「下边框消失」的真凶。 -->
            <Border x:Name='bd' CornerRadius='6' Background='{TemplateBinding Background}'
                    BorderBrush='{DynamicResource BtnBorder}' BorderThickness='1.34'
                    SnapsToDevicePixels='True' RenderOptions.EdgeMode='Aliased'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Background' Value='{DynamicResource PressBg}'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.45'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='PBtnPrimary' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Background' Value='{DynamicResource Accent}'/>
      <Setter Property='BorderBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='Foreground' Value='#FFFFFF'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='Button'>
            <Border x:Name='bd' CornerRadius='6' Background='{TemplateBinding Background}'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsMouseOver' Value='True'>
                <Setter TargetName='bd' Property='Opacity' Value='0.9'/>
              </Trigger>
              <Trigger Property='IsPressed' Value='True'>
                <Setter TargetName='bd' Property='Opacity' Value='0.78'/>
              </Trigger>
              <Trigger Property='IsEnabled' Value='False'>
                <Setter TargetName='bd' Property='Opacity' Value='0.45'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- 第三按钮：普通外观；必须显式关掉 IsCancel，否则 Esc 会在取消按钮之间打架 -->
    <Style x:Key='PBtnFlat' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='IsCancel' Value='False'/>
    </Style>";

        internal static readonly string Prompt = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Margin='18' CornerRadius='10' Background='{DynamicResource DialogBg}'
        BorderBrush='{DynamicResource BorderColor}' BorderThickness='1'>
  <Border.Resources>" + BtnStyles + @"  </Border.Resources>
  <Border.Effect>
    <DropShadowEffect BlurRadius='28' ShadowDepth='6' Opacity='0.26' Color='#000000'/>
  </Border.Effect>
  <Grid Margin='26,24,26,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='20' Height='20' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.7' Fill='Transparent'
              Data='M 10,1 L 19,5 L 19,12 C 19,17 15,20 10,22 C 5,20 1,17 1,12 L 1,5 Z M 6,11 L 9,14 L 14,8'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='需要管理员权限' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock Text='部分系统项目当前为只读状态' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <TextBlock Grid.Row='1' Margin='0,18,0,0' TextWrapping='Wrap' LineHeight='21' FontSize='12.5'
               Foreground='{DynamicResource TextSecondary}'
               Text='修改系统级服务、计划任务以及所有用户的启动项需要管理员权限。以管理员身份重新启动后即可执行启用、禁用、启动、停止等操作；选择普通权限继续则仅可查看列表。'/>
    <StackPanel Grid.Row='2' Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,22,0,0'>
      <Button x:Name='BtnContinue' Content='以普通权限继续' Style='{StaticResource PBtn}'/>
      <Button x:Name='BtnRestartAdmin' Content='以管理员身份重启' Style='{StaticResource PBtnPrimary}'/>
    </StackPanel>
  </Grid>
</Border>";

        internal static readonly string AiSettings = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Margin='16' CornerRadius='10' Background='{DynamicResource DialogBg}'
        BorderBrush='{DynamicResource BorderColor}' BorderThickness='1'>
  <Border.Resources>" + BtnStyles + @"    <Style x:Key='PTiny' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Height' Value='28'/>
      <Setter Property='MinWidth' Value='50'/>
      <Setter Property='Padding' Value='10,0'/>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='Margin' Value='0,0,6,0'/>
    </Style>
    <Style x:Key='AField' TargetType='TextBox'>
<Setter Property='Height' Value='34'/>
      <Setter Property='FontSize' Value='12.5'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='CaretBrush' Value='{DynamicResource Accent}'/>
      <Setter Property='VerticalContentAlignment' Value='Center'/>
      <Setter Property='Template'>
        <Setter.Value>
          <ControlTemplate TargetType='TextBox'>
            <Border x:Name='bd' CornerRadius='6' Background='{DynamicResource BtnBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1' Padding='10,0'>
              <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsKeyboardFocusWithin' Value='True'>
                <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key='ALabel' TargetType='TextBlock'>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Margin' Value='0,0,0,6'/>
    </Style>
    <Style x:Key='PList' TargetType='ListBox'>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
      <Setter Property='ItemContainerStyle'>
        <Setter.Value>
          <Style TargetType='ListBoxItem'>
            <Setter Property='Margin' Value='0,0,0,3'/>
            <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
              <Setter.Value>
                <ControlTemplate TargetType='ListBoxItem'>
                  <Border x:Name='bd' CornerRadius='6' Background='Transparent' Padding='9,6'>
                    <ContentPresenter/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                      <Setter TargetName='bd' Property='Background' Value='{DynamicResource HoverBg}'/>
                    </Trigger>
                    <Trigger Property='IsSelected' Value='True'>
                      <Setter TargetName='bd' Property='Background' Value='{DynamicResource NavSelectedBg}'/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
        </Setter.Value>
      </Setter>
    </Style>
  </Border.Resources>
  <Border.Effect>
    <DropShadowEffect BlurRadius='28' ShadowDepth='6' Opacity='0.26' Color='#000000'/>
  </Border.Effect>
  <Grid Margin='24,22,24,26'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,18'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='20' Height='20' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.6' Fill='Transparent'
              Data='M 10,2.4 L 11.9,8.1 L 17.6,10 L 11.9,11.9 L 10,17.6 L 8.1,11.9 L 2.4,10 L 8.1,8.1 Z'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='配置 AI' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock Text='最多保存 5 套接口配置，同一时间只启用一套；用于 AI 分析与批量 AI 建议'
                   FontSize='11.5' Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>

    <Grid Grid.Row='1'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='216'/>
        <ColumnDefinition Width='*'/>
      </Grid.ColumnDefinitions>

      <StackPanel Grid.Column='0' Margin='0,0,16,0'>
        <TextBlock Text='接口配置' Style='{StaticResource ALabel}'/>
        <Border Height='288' CornerRadius='8' Background='{DynamicResource CardBg}'
                BorderBrush='{DynamicResource BorderColor}' BorderThickness='1' Padding='5'>
          <ListBox x:Name='FldList' Style='{StaticResource PList}'>
            <ListBox.ItemTemplate>
              <DataTemplate>
                <StackPanel>
                  <StackPanel Orientation='Horizontal'>
                    <TextBlock Text='{Binding Title}' FontSize='12.5' Foreground='{DynamicResource TextPrimary}'
                               TextTrimming='CharacterEllipsis' MaxWidth='118'/>
                    <Border CornerRadius='7' Padding='5,1' Margin='6,0,0,0'
                            Background='{DynamicResource NavSelectedBg}'
                            Visibility='{Binding ActiveTag}'>
                      <TextBlock Text='使用中' FontSize='10' Foreground='{DynamicResource Accent}'/>
                    </Border>
                  </StackPanel>
                  <TextBlock Text='{Binding Sub}' FontSize='10.5' Foreground='{DynamicResource TextSecondary}'
                             TextTrimming='CharacterEllipsis' Margin='0,2,0,0'/>
                  <TextBlock Text='{Binding Stats}' ToolTip='{Binding StatsTip}' FontSize='10'
                             Foreground='{DynamicResource TextSecondary}'
                             TextTrimming='CharacterEllipsis' Margin='0,3,0,0'/>
                </StackPanel>
              </DataTemplate>
            </ListBox.ItemTemplate>
          </ListBox>
        </Border>
        <StackPanel Orientation='Horizontal' Margin='0,10,0,0'>
          <Button x:Name='BtnNew' Content='新增' Style='{StaticResource PTiny}'/>
          <Button x:Name='BtnDel' Content='删除' Style='{StaticResource PTiny}'/>
          <Button x:Name='BtnUse' Content='设为当前' Style='{StaticResource PTiny}'/>
        </StackPanel>
      </StackPanel>

      <StackPanel Grid.Column='1'>
        <TextBlock Text='配置名称' Style='{StaticResource ALabel}'/>
        <TextBox x:Name='FldName' Style='{StaticResource AField}' Margin='0,0,0,12'
                 ToolTip='用于在列表里区分，可留空（自动命名为「配置 N」）'/>
        <TextBlock Text='接口地址（OpenAI Chat Completions 协议，/v1/chat/completions 结尾）'
                   Style='{StaticResource ALabel}'/>
        <TextBox x:Name='FldEndpoint' Style='{StaticResource AField}' Margin='0,0,0,12'
                 ToolTip='例如 https://api.deepseek.com/v1/chat/completions'/>
        <TextBlock Text='API Key' Style='{StaticResource ALabel}'/>
        <Grid Margin='0,0,0,12'>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='Auto'/>
          </Grid.ColumnDefinitions>
          <TextBox x:Name='FldKey' Grid.Column='0' Style='{StaticResource AField}'
                   ToolTip='与程序同目录的 PCToolsAI.sqlite（DPAPI 按当前用户加密存储，文件权限亦已限制为当前用户）'/>
          <Button x:Name='BtnClearKey' Grid.Column='1' Content='清除' Style='{StaticResource PTiny}'
                  Margin='8,0,0,0' ToolTip='清除已保存的 API Key（保存后生效）'/>
        </Grid>
        <TextBlock Text='模型名称' Style='{StaticResource ALabel}'/>
        <TextBox x:Name='FldModel' Style='{StaticResource AField}'
                 ToolTip='例如 deepseek-chat / gpt-4o-mini / qwen-plus'/>
        <StackPanel Orientation='Horizontal' HorizontalAlignment='Right' Margin='0,14,0,0'>
          <Button x:Name='BtnSaveOne' Content='保存此配置' Style='{StaticResource PBtn}'
                  ToolTip='只保存当前这套配置，不关闭窗口（底部「保存」是保存全部并关闭）'/>
        </StackPanel>
      </StackPanel>
    </Grid>

    <StackPanel Grid.Row='2' x:Name='AiStatusHost' Visibility='Collapsed' Margin='0,12,0,0'>
      <TextBlock x:Name='AiStatus' FontSize='11.5' TextWrapping='Wrap' LineHeight='18'
                 Foreground='{DynamicResource TextSecondary}'/>
    </StackPanel>

    <Border Grid.Row='3' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0'
            Padding='0,14,0,0' Margin='0,14,0,0'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='BtnCancel' Content='取消' Style='{StaticResource PBtn}'/>
        <Button x:Name='BtnTest' Content='测试连接' Style='{StaticResource PBtn}'/>
        <Button x:Name='BtnSave' Content='保存' Style='{StaticResource PBtnPrimary}'/>
      </StackPanel>
    </Border>
  </Grid>
</Border>";

        internal static readonly string AiResult = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Margin='16' CornerRadius='10' Background='{DynamicResource DialogBg}'
        BorderBrush='{DynamicResource BorderColor}' BorderThickness='1'>
  <Border.Resources>" + BtnStyles + @"    <Style x:Key='AOutput' TargetType='TextBox'>
      <Setter Property='IsReadOnly' Value='True'/>
      <Setter Property='BorderThickness' Value='0'/>
      <Setter Property='Background' Value='Transparent'/>
      <Setter Property='Foreground' Value='{DynamicResource TextPrimary}'/>
      <Setter Property='FontSize' Value='13'/>
      <Setter Property='Padding' Value='0'/>
      <Setter Property='TextWrapping' Value='Wrap'/>
      <Setter Property='AcceptsReturn' Value='True'/>
      <Setter Property='VerticalScrollBarVisibility' Value='Auto'/>
      <Setter Property='VerticalContentAlignment' Value='Top'/>
    </Style>
    <Style x:Key='SectionLabel' TargetType='TextBlock'>
      <Setter Property='FontSize' Value='11.5'/>
      <Setter Property='FontWeight' Value='SemiBold'/>
      <Setter Property='Foreground' Value='{DynamicResource TextSecondary}'/>
      <Setter Property='Margin' Value='2,0,0,8'/>
    </Style>
  </Border.Resources>
  <Border.Effect>
    <DropShadowEffect BlurRadius='28' ShadowDepth='6' Opacity='0.26' Color='#000000'/>
  </Border.Effect>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='20' Height='20' Stretch='Uniform' Stroke='{DynamicResource Accent}' StrokeThickness='1.6' Fill='Transparent'
              Data='M 10,2.4 L 11.9,8.1 L 17.6,10 L 11.9,11.9 L 10,17.6 L 8.1,11.9 L 2.4,10 L 8.1,8.1 Z'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock Text='AI 分析' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='AiSubject' FontSize='11.5' TextTrimming='CharacterEllipsis'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <Grid Grid.Row='1'>
      <StackPanel>
        <TextBlock Text='项目介绍' Style='{StaticResource SectionLabel}'/>
        <Border Height='132' CornerRadius='8' Background='{DynamicResource CardBg}'
                BorderBrush='{DynamicResource BorderColor}' BorderThickness='1' Padding='14,12'
                Margin='0,0,0,14'>
          <TextBox x:Name='AiBody' Style='{StaticResource AOutput}'/>
        </Border>
        <TextBlock x:Name='AdviceHeading' Text='处理建议' Style='{StaticResource SectionLabel}'/>
        <!-- 单份时只看右列并横跨整行；库里有两份时左右并排对比 -->
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='*'/>
            <ColumnDefinition Width='16'/>
            <ColumnDefinition Width='*'/>
          </Grid.ColumnDefinitions>
          <StackPanel x:Name='OldPane' Grid.Column='0' Visibility='Collapsed'>
            <TextBlock x:Name='AiCapOld' FontSize='11' FontWeight='SemiBold' Margin='2,0,0,8'
                       Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'/>
            <Border Height='172' CornerRadius='8' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1' Padding='14,12'>
              <DockPanel>
                <TextBlock x:Name='AdviceTagOld' DockPanel.Dock='Top' FontSize='15' FontWeight='SemiBold'
                           Margin='0,0,0,6' Visibility='Collapsed'/>
                <TextBox x:Name='AiAdviceOld' Style='{StaticResource AOutput}'/>
              </DockPanel>
            </Border>
          </StackPanel>
          <StackPanel x:Name='NewPane' Grid.Column='0' Grid.ColumnSpan='3'>
            <TextBlock x:Name='AiCapNew' FontSize='11' FontWeight='SemiBold' Margin='2,0,0,8'
                       Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'
                       Visibility='Collapsed'/>
            <Border Height='172' CornerRadius='8' Background='{DynamicResource CardBg}'
                    BorderBrush='{DynamicResource BorderColor}' BorderThickness='1' Padding='14,12'>
              <DockPanel>
                <TextBlock x:Name='AdviceTag' DockPanel.Dock='Top' FontSize='15' FontWeight='SemiBold'
                           Margin='0,0,0,6' Visibility='Collapsed'/>
                <TextBox x:Name='AiAdvice' Style='{StaticResource AOutput}'/>
              </DockPanel>
            </Border>
          </StackPanel>
        </Grid>
      </StackPanel>
      <!-- 浮层：请求期间盖住两个信息框。默认收起，构造代码按需打开，
           避免 FindName 之前出异常时窗口挂着假加载态 -->
      <Border x:Name='AiLoading' Background='{DynamicResource DialogBg}' CornerRadius='8' Visibility='Collapsed'>
        <StackPanel HorizontalAlignment='Center' VerticalAlignment='Center'>
          <Grid x:Name='AiSpinHost' Width='28' Height='28' HorizontalAlignment='Center'>
            <Ellipse Width='24' Height='24' Stroke='{DynamicResource BorderColor}' StrokeThickness='2.4'/>
            <Path x:Name='AiSpinner' Width='24' Height='24' Stretch='None'
                  Stroke='{DynamicResource Accent}' StrokeThickness='2.4'
                  StrokeStartLineCap='Round' StrokeEndLineCap='Round'
                  Data='M 13,1.4 A 11.6,11.6 0 1 1 1.4,13'/>
          </Grid>
          <TextBlock x:Name='AiLoadingText' Text='正在获取 AI 数据…' FontSize='12.5'
                     HorizontalAlignment='Center' Margin='0,14,0,0' MaxWidth='420'
                     TextWrapping='Wrap' TextAlignment='Center' LineHeight='20'
                     Foreground='{DynamicResource TextSecondary}'/>
        </StackPanel>
      </Border>
    </Grid>
    <Border Grid.Row='2' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0'
            Padding='0,14,0,0' Margin='0,16,0,0'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='*'/>
          <ColumnDefinition Width='Auto'/>
          <ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <TextBlock x:Name='AiMeta' Grid.Column='0' FontSize='11' VerticalAlignment='Center'
                   Foreground='{DynamicResource TextSecondary}' TextTrimming='CharacterEllipsis'
                   Margin='0,0,12,0'/>
        <TextBlock x:Name='AiUsage' Grid.Column='1' FontSize='11' VerticalAlignment='Center'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,0,14,0'/>
        <StackPanel Grid.Column='2' Orientation='Horizontal' HorizontalAlignment='Right'>
          <Button x:Name='BtnAiRefresh' Content='更新AI分析' Style='{StaticResource PBtn}'/>
          <Button x:Name='BtnAiClose' Content='关闭' Style='{StaticResource PBtnPrimary}'/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Border>";

        internal static readonly string Confirm = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Margin='16' CornerRadius='10' Background='{DynamicResource DialogBg}'
        BorderBrush='{DynamicResource BorderColor}' BorderThickness='1'>
  <Border.Resources>" + BtnStyles + @"    <!-- 确认框的两个本地差异：取消键 = Esc（IsCancel），确认键 = Enter（IsDefault） -->
    <Style x:Key='PBtnCancel' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='IsCancel' Value='True'/>
    </Style>
    <Style x:Key='PBtnYes' TargetType='Button' BasedOn='{StaticResource PBtnPrimary}'>
      <Setter Property='IsCancel' Value='False'/>
      <Setter Property='IsDefault' Value='True'/>
    </Style>
  </Border.Resources>
  <Border.Effect>
    <DropShadowEffect BlurRadius='28' ShadowDepth='6' Opacity='0.26' Color='#000000'/>
  </Border.Effect>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <TextBlock x:Name='DlgGlyph' Text='!' FontSize='21' FontWeight='SemiBold'
                   Foreground='{DynamicResource Accent}'
                   HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock x:Name='DlgTitle' Text='确认操作' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='DlgSubtitle' Text='此操作会立即生效' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <!-- 失败明细可能很长：限高 + 滚动，避免 SizeToContent=Height 把底部按钮顶出工作区 -->
    <ScrollViewer x:Name='DlgScroll' Grid.Row='1' VerticalScrollBarVisibility='Auto'
                  HorizontalScrollBarVisibility='Disabled' MaxHeight='420'>
      <TextBlock x:Name='DlgText' TextWrapping='Wrap' LineHeight='22' FontSize='12.5'
                 VerticalAlignment='Top'
                 Foreground='{DynamicResource TextSecondary}'/>
    </ScrollViewer>
    <Border Grid.Row='2' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0'
            Padding='0,14,0,0' Margin='0,16,0,0'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='BtnNo' Content='取消' Style='{StaticResource PBtnCancel}'/>
        <Button x:Name='BtnThird' Content='补全分析' Style='{StaticResource PBtnFlat}' Visibility='Collapsed'/>
        <Button x:Name='BtnYes' Content='确认' Style='{StaticResource PBtnYes}'/>
      </StackPanel>
    </Border>
  </Grid>
</Border>";

        // 批量操作进度：显示当前进度并可取消，避免长时间操作把界面卡死
        internal static readonly string Progress = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Margin='16' CornerRadius='10' Background='{DynamicResource DialogBg}'
        BorderBrush='{DynamicResource BorderColor}' BorderThickness='1'>
  <Border.Resources>" + BtnStyles + @"    <!-- 进度窗取消按钮独占一行右对齐，去掉按钮间的左外边距 -->
    <Style x:Key='PBtnPg' TargetType='Button' BasedOn='{StaticResource PBtn}'>
      <Setter Property='Margin' Value='0'/>
    </Style>
  </Border.Resources>
  <Border.Effect>
    <DropShadowEffect BlurRadius='28' ShadowDepth='6' Opacity='0.26' Color='#000000'/>
  </Border.Effect>
  <Grid Margin='24,22,24,24'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>
    <StackPanel Grid.Row='0' Orientation='Horizontal' Margin='0,0,0,16'>
      <Border Width='40' Height='40' CornerRadius='10' Background='{DynamicResource SelectionBg}'>
        <Path Width='19' Height='19' Stretch='Uniform' Stroke='{DynamicResource Accent}'
              StrokeThickness='1.6' Fill='Transparent' StrokeLineJoin='Round'
              Data='M 10,2 A 8,8 0 1 1 4.3,4.3 M 10,5.6 L 10,10 L 13,11.8'/>
      </Border>
      <StackPanel Margin='14,0,0,0' VerticalAlignment='Center'>
        <TextBlock x:Name='PgHead' Text='正在执行操作' FontSize='17' FontWeight='SemiBold'
                   Foreground='{DynamicResource TextPrimary}'/>
        <TextBlock x:Name='PgHint' Text='可以随时点击右下角「取消」，已完成的项目会保留' FontSize='11.5'
                   Foreground='{DynamicResource TextSecondary}' Margin='0,3,0,0'/>
      </StackPanel>
    </StackPanel>
    <TextBlock x:Name='PgText' Grid.Row='1' Text='准备中…' FontSize='12.5' TextWrapping='Wrap'
               Foreground='{DynamicResource TextSecondary}' Margin='0,0,0,10'/>
    <ProgressBar x:Name='PgBar' Grid.Row='2' Height='6' Minimum='0' Maximum='1' Value='0'
                 Background='{DynamicResource BorderColor}' Foreground='{DynamicResource Accent}'>
      <ProgressBar.Template>
        <ControlTemplate TargetType='ProgressBar'>
          <Border Background='{TemplateBinding Background}' CornerRadius='3' ClipToBounds='True'>
            <Grid x:Name='PART_Track'>
              <Border x:Name='PART_Indicator' HorizontalAlignment='Left' CornerRadius='3'
                      Background='{TemplateBinding Foreground}'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ProgressBar.Template>
    </ProgressBar>
    <!-- 本次累计 Token：默认折叠，只有逐项 AI 分析会展开，其它批量操作窗口高度不变 -->
    <TextBlock x:Name='PgTokens' Grid.Row='3' Text='' FontSize='11' Visibility='Collapsed'
               HorizontalAlignment='Right' Margin='0,8,0,0' TextTrimming='CharacterEllipsis'
               Foreground='{DynamicResource TextSecondary}'/>
    <Border Grid.Row='4' BorderBrush='{DynamicResource BorderColor}' BorderThickness='0,1,0,0'
            Padding='0,14,0,0' Margin='0,18,0,0'>
      <StackPanel Orientation='Horizontal' HorizontalAlignment='Right'>
        <Button x:Name='BtnCancel' Content='取消' Style='{StaticResource PBtnPg}'/>
      </StackPanel>
    </Border>
  </Grid>
</Border>";
    }

}
